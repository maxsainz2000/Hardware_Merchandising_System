' Merchandising.Contracts.Sales.CashierSessionResponse
'
' A cashier session - Open (a sale may attach to it) or Closed (immutable,
' spec section 10.3). DeclaredCash/CalculatedCash/CashVariance already exist
' on CashierSessions (0011_pos.sql - migration 0012 declares them ahead of
' the card that drives them, the same StockCounts/StockAdjustments
' precedent) but stay Nothing until P5-05's daily closing sets them; this
' card's CloseAsync never touches them.

Imports System.Text.Json.Serialization

Namespace Sales

    Public NotInheritable Class CashierSessionResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        <JsonPropertyName("openedByUserId")>
        Public Property OpenedByUserId As Integer

        <JsonPropertyName("closedByUserId")>
        Public Property ClosedByUserId As Integer?

        <JsonPropertyName("openingFloat")>
        Public Property OpeningFloat As Decimal

        <JsonPropertyName("declaredCash")>
        Public Property DeclaredCash As Decimal?

        <JsonPropertyName("calculatedCash")>
        Public Property CalculatedCash As Decimal?

        <JsonPropertyName("cashVariance")>
        Public Property CashVariance As Decimal?

        <JsonPropertyName("status")>
        Public Property Status As String = String.Empty

        <JsonPropertyName("openedAtUtc")>
        Public Property OpenedAtUtc As DateTime

        <JsonPropertyName("closedAtUtc")>
        Public Property ClosedAtUtc As DateTime?

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
