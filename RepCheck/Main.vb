﻿Imports System.IO
Imports System.Threading.Tasks
Imports System.Collections.Concurrent
Imports System.Data.SqlClient
Imports System.Configuration
Imports log4net


Module Main
    Dim FileStoreObjDict As ConcurrentDictionary(Of Guid, String) = Nothing
    Dim FileStoreObjs As ConcurrentBag(Of Guid) = Nothing
    Dim DBObjs As ConcurrentBag(Of Guid) = Nothing

    Dim FilesNotInDB As List(Of Guid) = Nothing
    Dim DBRecsNotInFS As List(Of Guid) = Nothing

    Dim SharedExitCode As ExitCode = ExitCode.SUCCESS

    Private Log As ILog = LogManager.GetLogger("WCFRestAppender")

    Function Main() As Integer
        Dim args() As String = System.Environment.GetCommandLineArgs()

        Try
            Dim Options As Options = GetOptions(args)
            'print help if /? is passed
            If (Options.Help) Then
                Console.WriteLine("Purpose: find missing and orphaned document files in a FileNet system.")
                Console.WriteLine("")
                Console.WriteLine("Usage:")
                Console.WriteLine("RepCheck.exe /s dbserver\instance /os objectstore [/fs filestore] /ddb docDB /rdb reportDB [/t maxthreads] [/y]")
                Console.WriteLine("")
                Console.WriteLine("/fs is an optional parameter for object stores with more than one file store.")
                Console.WriteLine("/t is an optional parameter for specifying a max thread count. MaxThreads setting in the config file will be used if this switch is not specified.")
                Console.WriteLine("/y Indicates to automatically upload report information to server (for unattended operation).")
                Return ExitCode.SUCCESS
            End If
            'This allows for tuning. Without this we could technically fire off more threads than storage can handle.
            If (Options.MaxThreads = 0) Then
                MaxThreads = ConfigurationManager.AppSettings("MaxThreads")
            End If

            Dim FileStores As List(Of FileStoreInfo) = GetFileStoreInfos(Options.DatabaseServer, Options.ObjectStoreDatabaseName)

            'Filter on file store name if supplied.
            If (Not String.IsNullOrWhiteSpace(FileStoreName)) Then
                FileStores = FileStores.Where(Function(fs) fs.FileStoreName = FileStoreName).ToList()
                If (FileStores.Count <> 1) Then
                    Throw New ArgumentException("Invalid file store name.")
                End If
            End If

            For Each fs As FileStoreInfo In FileStores
                Dim NewReport As RepCheckReport = RunReport(ObjectStoreName, ObjectStoreDatabaseName, ReportDatabaseName, DatabaseServer, fs.FileStoreName, fs.FileStoreId, fs.FileStoreLocation)

                If (Not AutoUpload) Then
                    Console.WriteLine("Upload report data to server? (Y|N)")
                    If (Console.ReadLine().ToUpper() = "Y") Then
                        AutoUpload = True
                    End If
                End If

                If (AutoUpload) Then
                    Log.Debug("Uploading report data.")
                    Try
                        Dim entities As ReportEntities = New ReportEntities("metadata=res://*/ReportModel.csdl|res://*/ReportModel.ssdl|res://*/ReportModel.msl;provider=System.Data.SqlClient;provider connection string=""data source=" &
                                                                    DatabaseServer & ";initial catalog=" & ReportDatabaseName & ";integrated security=True;MultipleActiveResultSets=True;App=EntityFramework""")
                        entities.RepCheckReports.Add(NewReport)
                        entities.SaveChanges()

                        If (ComparePrevious) Then
                            Dim PrevReport As RepCheckReport = entities.RepCheckReports.Where(
                                Function(r) r.ObjectStoreName = ObjectStoreName And "" = FileStoreName And r.ReportID <> NewReport.ReportID).OrderByDescending(
                                Function(r) r.ReportDate).FirstOrDefault()
                            If (PrevReport IsNot Nothing) Then
                                Dim NewOrphans As List(Of OrphanedFile) = NewReport.OrphanedFiles.Except(PrevReport.OrphanedFiles, New OrphanComparer(Of OrphanedFile))
                            Else
                                Log.Info("No previous report for this object store and file store.")
                            End If
                        End If
                    Catch ex As Exception
                        Log.Error("Error adding report data. ", ex)
                        SharedExitCode = ExitCode.ERROR_DATABASE_FAILURE
                        Throw
                    End Try
                End If
            Next
        Catch aex As ArgumentException
            Log.Error(aex)
            Return ExitCode.ERROR_BAD_ARGUMENTS
        Catch ex As Exception
            Log.Error(ex)
            If SharedExitCode = ExitCode.SUCCESS Then
                'Exception was thrown outside of try blocks. Return generic error.
                SharedExitCode = ExitCode.ERROR_INVALID_FUNCTION
            End If
            Return SharedExitCode
        End Try

        Console.WriteLine("Finished.")
        Return SharedExitCode
    End Function

    Private Function GetOptions(args As String()) As Options
        Dim ret As Options = New Options()

        For Each arg As String In args
            Select Case arg
                Case "/os"
                    ret.ObjectStoreName = args(Array.IndexOf(args, arg) + 1)
                Case "/fs"
                    ret.FileStoreName = args(Array.IndexOf(args, arg) + 1)
                Case "/ddb"
                    ret.ObjectStoreDatabaseName = args(Array.IndexOf(args, arg) + 1)
                Case "/rdb"
                    ret.ReportDatabaseName = args(Array.IndexOf(args, arg) + 1)
                Case "/s"
                    ret.DatabaseServer = args(Array.IndexOf(args, arg) + 1)
                Case "/y"
                    ret.AutoUpload = True
                Case "/t"
                    ret.MaxThreads = CInt(args(Array.IndexOf(args, arg) + 1))
                Case "/?"
                    ret.Help = True
            End Select
        Next

        'Check arguments
        If (String.IsNullOrWhiteSpace(ObjectStoreName) OrElse String.IsNullOrWhiteSpace(ObjectStoreDatabaseName) OrElse
                String.IsNullOrWhiteSpace(DatabaseServer) OrElse String.IsNullOrWhiteSpace(ReportDatabaseName)) Then
            Throw New ArgumentException("Invalid argument.")
        End If
    End Function

    Private Function RunReport(ObjectStoreName As String, ObjectStoreDatabaseName As String, ReportDatabaseName As String,
                          DatabaseServer As String, FileStoreName As String, FileStoreId As String, FileStoreLocation As String) As RepCheckReport

        Log.Info("Running RepCheck report for Object Store: " & ObjectStoreName & " with File Store: " & FileStoreName)

        'This is to reset in the event of multiple file stores.
        FileStoreObjDict = New ConcurrentDictionary(Of Guid, String)
        FileStoreObjs = New ConcurrentBag(Of Guid)
        DBObjs = New ConcurrentBag(Of Guid)

        FilesNotInDB = New List(Of Guid)()
        DBRecsNotInFS = New List(Of Guid)()

        'If there is more than on file store we may have already used 30+ GB of RAM. Let it be reclaimed before filling it again.
        GC.Collect()

        Dim start As DateTime = DateTime.Now
        Dim dbdur As Decimal
        Dim fsdur As Decimal
        Dim procdur As Decimal
        Dim totduration As Decimal

        Dim MyTasks(1) As Task

        Dim GetFilesTask As Task = New Task(Sub()
                                                Dim exceptions As ConcurrentBag(Of Exception) = New ConcurrentBag(Of Exception)
                                                Dim root As DirectoryInfo = New DirectoryInfo(FileStoreLocation)
                                                GetFiles(root, New ParallelOptions() With {.MaxDegreeOfParallelism = MaxThreads}, exceptions)
                                                'Check if exceptions was thrown while getting files.
                                                If (exceptions.Count > 0) Then
                                                    Throw New AggregateException("Exception during file acquisition. Aborting.", exceptions)
                                                End If
                                                Dim ending As DateTime = DateTime.Now
                                                fsdur = ((ending.Ticks - start.Ticks) / 10000000)
                                                Log.Debug("File acquisition took " & fsdur & " seconds.")
                                            End Sub)
        MyTasks(0) = GetFilesTask

        Dim GetIDsTask As Task = New Task(Sub()
                                              Dim connectionString As String = "Server=" & DatabaseServer & ";Database=" & ObjectStoreDatabaseName & ";Integrated Security=true"

                                              'Get all relevant doc GUIDs as fast as possible
                                              Using connection As New SqlConnection(connectionString)
                                                  Dim command As New SqlCommand("select dv.object_id from docversion dv where dv.version_status <> 3 and (dv.storage_area_id = '" & FileStoreId & "' or dv.storage_location = 'FNFS:/{" & FileStoreId & "}')", connection)
                                                  Try
                                                      connection.Open()
                                                      Dim dataReader As SqlDataReader = command.ExecuteReader()
                                                      Do While dataReader.Read()
                                                          DBObjs.Add(dataReader.GetGuid(0))
                                                      Loop
                                                  Catch ex As Exception
                                                      Log.Error(ex)
                                                      SharedExitCode = ExitCode.ERROR_DATABASE_FAILURE
                                                      Throw
                                                  End Try

                                                  Dim ending As DateTime = DateTime.Now
                                                  dbdur = ((ending.Ticks - start.Ticks) / 10000000)
                                                  Log.Debug("DB acquisition took " & dbdur & " seconds.")
                                              End Using
                                          End Sub)

        MyTasks(1) = GetIDsTask

        'kick off file and DB operations in parallel
        GetFilesTask.Start()
        GetIDsTask.Start()

        Try
            Task.WaitAll(MyTasks)
        Catch ex As AggregateException
            'An exception was generated in at least one of the tasks. No need to run through all of them.
            'Just report the last one and let the admin work it out from there.
            Log.Error("Error aquiring data to generate report.", ex)
            If SharedExitCode = ExitCode.SUCCESS Then
                SharedExitCode = ExitCode.ERROR_PROCESS_ABORTED
            End If
            Throw
        End Try

        Log.Debug("Retrieved " & FileStoreObjs.Count & " files and " & DBObjs.Count & " DB records.")

        Dim startproc As DateTime = Now

        'Find all files that are not referenced by a document in the database
        FilesNotInDB.AddRange(FileStoreObjs.AsParallel().Except(DBObjs.AsParallel()))
        'Find all DB records that do not have a file.
        DBRecsNotInFS.AddRange(DBObjs.AsParallel().Except(FileStoreObjs.AsParallel()))

        'Write out and log results
        Log.Info("FileStore: " & FileStoreName & " has " & FilesNotInDB.Count & " orphans.")
        Log.Info("ObjectStore: " & ObjectStoreName & "  has " & DBRecsNotInFS.Count & " missing files.")

        Dim procending As DateTime = DateTime.Now
        procdur = ((procending.Ticks - startproc.Ticks) / 10000000)
        Log.Debug("Processing time was " & procdur & " seconds.")
        Try
            Dim NewReport As RepCheckReport = New RepCheckReport() With {
                    .ReportDate = Now,
                    .ObjectStoreName = ObjectStoreName,
                    .DBTime = dbdur,
                    .FSTime = fsdur,
                    .ProcessTime = procdur
                }

            For Each orphan In FilesNotInDB
                Dim path As String = FileStoreObjDict(orphan)
                Dim CreateDate As DateTime = New FileInfo(path).CreationTime
                NewReport.OrphanedFiles.Add(New OrphanedFile() With {.RepCheckReport = NewReport, .Path = path, .CreationTime = CreateDate})
            Next

            For Each missing In DBRecsNotInFS
                NewReport.MissingFiles.Add(New MissingFile() With {.RepCheckReport = NewReport, .ObjectID = missing.ToString("D")})
            Next

            Return NewReport
        Catch ex As Exception
            Log.Error(ex)
            SharedExitCode = ExitCode.ERROR_DATABASE_FAILURE
            Throw
        End Try
    End Function

    Private Sub GetFiles(folder As DirectoryInfo, options As ParallelOptions, exceptions As ConcurrentBag(Of Exception))
        Try
            'Abort if any thread has thrown exception
            If (exceptions.Count > 0) Then
                Exit Sub
            End If

            Dim folders As DirectoryInfo() = Nothing
            Try
                'This throws expeption if high IOPS creates latency above 20ms
                folders = folder.GetDirectories()
            Catch fex As Exception
                If (fex.Message.Contains("Windows cannot find the network path.")) Then
                    'It is expensive to fail the entire job. Try one more time to get directories.
                    Try
                        folders = folder.GetDirectories()
                    Catch fex2 As Exception
                        'Second error. Fail whole job
                        exceptions.Add(fex2)
                    End Try
                Else
                    'Not due to latency. Fail whole job
                    Dim message As String = "Error accessing contents of path: " & folder.FullName
                    Log.Error(message)
                    SharedExitCode = ExitCode.ERROR_ACCESS_DENIED
                    exceptions.Add(fex)
                End If
            End Try

            'Make sure the folders collection was populated.
            If (folders IsNot Nothing) Then
                Parallel.ForEach(folders, options,
                                 Sub(fol)
                                     GetFiles(fol, options, exceptions)
                                 End Sub)

                'One of threaded recursive children above may have thrown exception. If so, abort.
                If (exceptions.Count > 0) Then
                    Exit Sub
                End If

                Dim files As FileInfo() = folder.GetFiles()

                If (files.Count > 0) Then
                    Parallel.ForEach(files, options,
                                 Sub(fil)
                                     Try
                                         Dim name As String = fil.Name
                                         Dim start As Int16 = name.IndexOf("{")
                                         Dim ending As Int16 = name.IndexOf("}")
                                         If (start < 0 OrElse ending < 0) Then
                                             Console.WriteLine(name & " invalid file name.")
                                         Else
                                             start = start + 1
                                             Dim key As Guid = New Guid(name.Substring(start, ending - start))
                                             FileStoreObjs.Add(key)
                                             FileStoreObjDict.AddOrUpdate(key, fil.FullName, Function(a, b)
                                                                                                 Return b
                                                                                             End Function)
                                         End If
                                     Catch ex As Exception
                                         Log.Error("Error accessing file in folder: " & folder.FullName)
                                         SharedExitCode = ExitCode.ERROR_ACCESS_DENIED
                                         exceptions.Add(ex)
                                     End Try
                                 End Sub)
                End If
            End If
        Catch ex As Exception
            Log.Error(ex)
            SharedExitCode = ExitCode.ERROR_ACCESS_DENIED
            exceptions.Add(ex)
        End Try
    End Sub

    Private Function GetFileStoreInfos(DatabaseServer As String, ObjectStoreDatabaseName As String) As List(Of FileStoreInfo)
        Dim connectionString As String = "Server=" & DatabaseServer & ";Database=" & ObjectStoreDatabaseName & ";Integrated Security=true"
        Dim ret As List(Of FileStoreInfo) = New List(Of FileStoreInfo)()

        Using connection As New SqlConnection(connectionString)
            Dim command As New SqlCommand("SELECT s.display_name, s.object_id, s.fs_ads_path, c.symbolic_name FROM storageclass s " &
                                          "INNER JOIN classdefinition c on s.object_class_id = c.object_id " &
                                          "WHERE (c.symbolic_name = 'FileStorageArea' OR c.symbolic_name = 'FixedStorageArea')", connection)
            Try
                connection.Open()
                Dim dataReader As SqlDataReader = command.ExecuteReader()
                Do While dataReader.Read()
                    'The following section is equired until access to GCD can be done to determine fixed content device path.
                    Dim fileStoreLocation As String
                    If (dataReader.GetString(3) = "FixedStorageArea") Then
                        fileStoreLocation = ConfigurationManager.AppSettings(dataReader.GetGuid(1).ToString().ToUpper())
                    Else
                        fileStoreLocation = dataReader.GetString(2) & "\content"
                    End If
                    'end of section
                    ret.Add(New FileStoreInfo() With {
                            .FileStoreName = dataReader(0).ToString(),
                            .FileStoreId = dataReader(1).ToString().ToUpper(),
                            .FileStoreLocation = fileStoreLocation})
                Loop
            Catch ex As Exception
                Log.Error(ex)
                SharedExitCode = ExitCode.ERROR_DATABASE_FAILURE
                Throw
            End Try
        End Using

        Return ret
    End Function
End Module
