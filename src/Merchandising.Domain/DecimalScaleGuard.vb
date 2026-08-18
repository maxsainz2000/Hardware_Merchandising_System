''' <summary>
''' ADR-004.1: the database is never trusted to round. Money and quantity
''' values are validated and explicitly rounded to their storage scale here,
''' at the API boundary, before a parameter is ever bound.
''' </summary>
''' <remarks>
''' <c>STRICT_TRANS_TABLES</c> makes an over-<em>range</em> decimal an error
''' (ADR-003.2), but an over-<em>scale</em> decimal is still only a silent
''' <c>Note 1265</c> - the server rounds it and reports success. A correctly
''' -scaled stored value therefore proves nothing about whether this guard
''' ran; it must be exercised directly, not verified by reading data back.
''' </remarks>
Public NotInheritable Class DecimalScaleGuard

    ''' <summary>Storage scale for money: <c>DECIMAL(19,4)</c>.</summary>
    Public Const MoneyScale As Integer = 4

    ''' <summary>Storage scale for quantity: <c>DECIMAL(19,3)</c>.</summary>
    Public Const QuantityScale As Integer = 3

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Validates a client-supplied money value is already at or below the
    ''' storage scale. Per ADR-004.1 this is a validation failure, not a
    ''' value to quietly round on the caller's behalf.
    ''' </summary>
    ''' <exception cref="ArgumentException"><paramref name="value"/> has more than <see cref="MoneyScale"/> decimal places.</exception>
    Public Shared Function EnsureMoneyScale(value As Decimal) As Decimal
        Return EnsureScale(value, MoneyScale)
    End Function

    ''' <summary>
    ''' Validates a client-supplied quantity value is already at or below the
    ''' storage scale. Per ADR-004.1 this is a validation failure, not a
    ''' value to quietly round on the caller's behalf.
    ''' </summary>
    ''' <exception cref="ArgumentException"><paramref name="value"/> has more than <see cref="QuantityScale"/> decimal places.</exception>
    Public Shared Function EnsureQuantityScale(value As Decimal) As Decimal
        Return EnsureScale(value, QuantityScale)
    End Function

    ''' <summary>
    ''' Rounds an API-computed money value to its storage scale, half away
    ''' from zero - matching how a cashier would check a receipt by hand, not
    ''' .NET's default banker's rounding.
    ''' </summary>
    Public Shared Function RoundMoney(value As Decimal) As Decimal
        Return Decimal.Round(value, MoneyScale, MidpointRounding.AwayFromZero)
    End Function

    ''' <summary>
    ''' Rounds an API-computed quantity value to its storage scale, half away
    ''' from zero.
    ''' </summary>
    Public Shared Function RoundQuantity(value As Decimal) As Decimal
        Return Decimal.Round(value, QuantityScale, MidpointRounding.AwayFromZero)
    End Function

    Private Shared Function EnsureScale(value As Decimal, scale As Integer) As Decimal

        Dim rounded As Decimal = Decimal.Round(value, scale, MidpointRounding.AwayFromZero)

        If rounded <> value Then
            Throw New ArgumentException(
                $"Value '{value}' exceeds the storage scale of {scale} decimal place(s). " &
                "Client-supplied money and quantity values must be rejected, not silently rounded (ADR-004.1).",
                NameOf(value))
        End If

        Return value

    End Function

End Class
