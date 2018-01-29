Public Class OrphanComparer
    Implements IEqualityComparer(Of OrphanedFile)

    Public Function Equals(x As OrphanedFile, y As OrphanedFile) As Boolean Implements IEqualityComparer(Of OrphanedFile).Equals
        Return x.Path = y.Path
    End Function

    Public Function GetHashCode(obj As OrphanedFile) As Integer Implements IEqualityComparer(Of OrphanedFile).GetHashCode
        Return obj.Path.GetHashCode()
    End Function
End Class
