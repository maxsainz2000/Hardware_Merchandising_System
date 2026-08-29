' Merchandising.Contracts.Inventory.StockBalanceResponse
'
' One item of GET /api/v1/inventory/stock (Stock.Read, P4-11). ReorderLevel is
' echoed alongside Quantity so a client never has to make a second call to
' Products just to know whether a listed balance is low - the same
' "reconcile rather than re-query" shaping the card asks for.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class StockBalanceResponse

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("sku")>
        Public Property Sku As String = String.Empty

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("isActive")>
        Public Property IsActive As Boolean

        <JsonPropertyName("quantity")>
        Public Property Quantity As Decimal

        <JsonPropertyName("reorderLevel")>
        Public Property ReorderLevel As Decimal

    End Class

End Namespace
