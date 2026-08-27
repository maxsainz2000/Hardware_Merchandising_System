' Merchandising.Contracts.Procurement.PurchaseOrderLineResponse
'
' One line of a purchase order, as returned by GET /api/v1/purchase-orders/{id}.
'
' ProductSku and ProductName are a LIVE JOIN against Products, not values
' captured on the line. Only PurchaseCost is captured (0008's own header
' explains why: a cost that drifted after the order was placed would restate
' what was agreed with the supplier). Spec section 10.2 requires historical
' transactions to keep displaying a product's name after deactivation, which
' a join satisfies today because a referenced product can never be deleted -
' FK_PurchaseOrderLines_Products makes that a server guarantee (ERROR 1451),
' proven at P3-02.

Imports System.Text.Json.Serialization

Namespace Procurement

    Public NotInheritable Class PurchaseOrderLineResponse

        <JsonPropertyName("id")>
        Public Property Id As Integer

        ''' <summary>Identity of this line within its order (1..N). Stable; Phase 4's receiving command addresses lines by it.</summary>
        <JsonPropertyName("lineNumber")>
        Public Property LineNumber As Integer

        <JsonPropertyName("productId")>
        Public Property ProductId As Integer

        <JsonPropertyName("productSku")>
        Public Property ProductSku As String = String.Empty

        <JsonPropertyName("productName")>
        Public Property ProductName As String = String.Empty

        <JsonPropertyName("orderedQuantity")>
        Public Property OrderedQuantity As Decimal

        <JsonPropertyName("purchaseCost")>
        Public Property PurchaseCost As Decimal

        ''' <summary>Accumulated across receipts by Phase 4. Zero on every order this phase can create.</summary>
        <JsonPropertyName("receivedQuantity")>
        Public Property ReceivedQuantity As Decimal

        <JsonPropertyName("rowVersion")>
        Public Property RowVersion As Long

        <JsonPropertyName("createdAtUtc")>
        Public Property CreatedAtUtc As DateTime

        <JsonPropertyName("updatedAtUtc")>
        Public Property UpdatedAtUtc As DateTime

    End Class

End Namespace
