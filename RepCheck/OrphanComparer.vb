﻿Public Class OrphanComparer
    Implements IEqualityComparer(Of OrphanedFile)

    Public Function Equals(x As OrphanedFile, y As OrphanedFile) As Boolean Implements IEqualityComparer(Of OrphanedFile).Equals
        Return x.
    End Function

    Public Function GetHashCode(obj As OrphanedFile) As Integer Implements IEqualityComparer(Of OrphanedFile).GetHashCode
        Throw New NotImplementedException()
    End Function
End Class
