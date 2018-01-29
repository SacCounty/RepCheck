Public Class MissingComparer
    Implements IEqualityComparer(Of MissingFile)

    Public Function Equals(x As MissingFile, y As MissingFile) As Boolean Implements IEqualityComparer(Of MissingFile).Equals
        Return x.ObjectID = y.ObjectID
    End Function

    Public Function GetHashCode(obj As MissingFile) As Integer Implements IEqualityComparer(Of MissingFile).GetHashCode
        Return obj.ObjectID.GetHashCode()
    End Function
End Class
