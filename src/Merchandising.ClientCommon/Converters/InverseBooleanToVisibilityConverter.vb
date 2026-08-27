' Merchandising.ClientCommon.Converters.InverseBooleanToVisibilityConverter
'
' Spec section 7 assigns "converters" to this project. The BCL's own
' BooleanToVisibilityConverter shows a panel when True; this is the other
' half - hiding the sign-in panel once a session exists is exactly as common
' as showing the workspace once it does, and every client will want both.

Imports System.Globalization
Imports System.Windows
Imports System.Windows.Data

Namespace Converters

    ''' <summary>True maps to Collapsed, False maps to Visible - the inverse of BooleanToVisibilityConverter.</summary>
    Public NotInheritable Class InverseBooleanToVisibilityConverter
        Implements IValueConverter

        Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object _
            Implements IValueConverter.Convert

            Dim flag As Boolean = TypeOf value Is Boolean AndAlso DirectCast(value, Boolean)
            Return If(flag, Visibility.Collapsed, Visibility.Visible)

        End Function

        Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object _
            Implements IValueConverter.ConvertBack

            Dim visibility As Visibility = If(TypeOf value Is Visibility, DirectCast(value, Visibility), Visibility.Visible)
            Return visibility <> Visibility.Visible

        End Function

    End Class

End Namespace
