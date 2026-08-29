' Merchandising.Contracts.Inventory.LowStockItemResponse
'
' One item of GET /api/v1/inventory/low-stock (LowStock.Review, P4-11). Every
' item returned is already known active and at-or-below its own reorder
' level - StockRepository.SearchLowStockAsync's WHERE clause, not a client
' filter - so this carries no IsActive flag the way StockBalanceResponse
' does; it would always read True.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class LowStockItemResponse

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("sku")>
        Public Property Sku As String = String.Empty

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("quantity")>
        Public Property Quantity As Decimal

        <JsonPropertyName("reorderLevel")>
        Public Property ReorderLevel As Decimal

    End Class

End Namespace
