''' <summary>
''' Shared input-validation helpers used across pages so the same rules apply everywhere.
''' </summary>
Public Module ValidationHelpers

    ''' <summary>
    ''' Returns True if <paramref name="email"/> looks like a syntactically valid address.
    ''' This is intentionally lightweight (no full RFC 5322) but rejects the obvious junk
    ''' that the old "contains @ and ." check let through, e.g. "@.", "a@b", "a@b.".
    ''' </summary>
    Public Function IsValidEmail(email As String) As Boolean
        If String.IsNullOrWhiteSpace(email) Then Return False

        Dim trimmed = email.Trim()

        ' Exactly one @, not at the very start or end.
        Dim atIndex = trimmed.IndexOf("@"c)
        If atIndex <= 0 OrElse atIndex <> trimmed.LastIndexOf("@"c) OrElse atIndex = trimmed.Length - 1 Then
            Return False
        End If

        Dim localPart = trimmed.Substring(0, atIndex)
        Dim domainPart = trimmed.Substring(atIndex + 1)

        If String.IsNullOrWhiteSpace(localPart) OrElse String.IsNullOrWhiteSpace(domainPart) Then
            Return False
        End If

        ' No whitespace anywhere.
        If trimmed.Any(Function(c) Char.IsWhiteSpace(c)) Then
            Return False
        End If

        ' Domain must contain a dot with non-empty labels on both sides, and a TLD of 2+ chars.
        Dim lastDot = domainPart.LastIndexOf("."c)
        If lastDot <= 0 OrElse lastDot = domainPart.Length - 1 Then
            Return False
        End If
        Dim tld = domainPart.Substring(lastDot + 1)
        If tld.Length < 2 Then
            Return False
        End If

        ' No consecutive dots and no leading/trailing dot in the domain.
        If domainPart.StartsWith(".") OrElse domainPart.EndsWith(".") OrElse domainPart.Contains("..") Then
            Return False
        End If

        Return True
    End Function

End Module
