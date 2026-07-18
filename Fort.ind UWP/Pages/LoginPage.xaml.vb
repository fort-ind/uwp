''' <summary>
''' Sign-in page: hands off to fort.social via MiAuth.
''' </summary>
Public NotInheritable Class LoginPage
    Inherits Page

    Public Sub New()
        Me.InitializeComponent()
    End Sub

    ''' <summary>
    ''' Handle sign-in button click
    ''' </summary>
    Private Async Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)
        ErrorText.Visibility = Visibility.Collapsed
        ShowLoading(True)

        Try
            Dim result = Await ProfileService.LoginWithMisskeyAsync()

            If result.Success Then
                ' Navigate back instead of creating new MainPage instance
                If Frame.CanGoBack Then
                    Frame.GoBack()
                Else
                    Frame.Navigate(GetType(MainPage))
                End If
            Else
                ShowError(result.Message)
            End If
        Catch ex As Exception
            Debug.WriteLine($"SignInButton_Click error: {ex}")
            ShowError("An error occurred. Please try again.")
        Finally
            ShowLoading(False)
        End Try
    End Sub

    ''' <summary>
    ''' Cancel a sign-in that's waiting on the browser
    ''' </summary>
    Private Sub CancelSignInButton_Click(sender As Object, e As RoutedEventArgs)
        MisskeyAuthService.CancelPendingSignIn()
    End Sub

    ''' <summary>
    ''' Skip sign-in and continue without an account
    ''' </summary>
    Private Sub SkipButton_Click(sender As Object, e As RoutedEventArgs)
        If Frame.CanGoBack Then
            Frame.GoBack()
        Else
            Frame.Navigate(GetType(MainPage))
        End If
    End Sub

    ''' <summary>
    ''' Show error message
    ''' </summary>
    Private Sub ShowError(message As String)
        ErrorText.Text = message
        ErrorText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>
    ''' Show/hide loading overlay
    ''' </summary>
    Private Sub ShowLoading(show As Boolean)
        LoadingOverlay.Visibility = If(show, Visibility.Visible, Visibility.Collapsed)
        SignInButton.IsEnabled = Not show
        SkipButton.IsEnabled = Not show
    End Sub

End Class
