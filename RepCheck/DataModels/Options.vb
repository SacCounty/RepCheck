Public Class Options
    Public Property DatabaseServer As String = String.Empty
    Public Property ObjectStoreDatabaseName As String = String.Empty
    Public Property ReportDatabaseName As String = String.Empty
    Public Property ObjectStoreName As String = String.Empty
    Public Property FileStoreName As String = String.Empty
    Public Property Help As Boolean = False
    Public Property AutoUpload As Boolean
    Public Property ComparePrevious As Boolean = True
    Public Property MaxThreads As Integer = 0
End Class
