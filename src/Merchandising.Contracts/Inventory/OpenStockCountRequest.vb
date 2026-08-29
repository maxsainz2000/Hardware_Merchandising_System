' Merchandising.Contracts.Inventory.OpenStockCountRequest
'
' POST /api/v1/stock-counts body. Opening a count carries no product data at
' all - spec section 10.2's "count session" is the header alone
' (CountedByUserId, CountedAtUtc); products and their counted quantities
' arrive one at a time through RecordStockCountLineRequest against the
' opened session's Id.
'
' P4-09 / ADR-007: IdempotencyKey required, canonical 36-character UUID -
' the same shape every other write command in this solution uses. A
' repeated key replays the original opened session rather than opening a
' second one.

Imports System.Text.Json.Serialization

Namespace Inventory

    Public NotInheritable Class OpenStockCountRequest

        <JsonPropertyName("idempotencyKey")>
        Public Property IdempotencyKey As String = String.Empty

    End Class

End Namespace
