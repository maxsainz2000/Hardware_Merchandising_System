' Merchandising.Domain.Entities.PurchaseOrder
'
' Plain data holder for a PurchaseOrders row
' (db/migrations/0008_purchase-orders.sql), matching Supplier.vb's shape -
' Domain depends on nothing (CLAUDE.md section 4).
'
' Status is the DOMAIN ENUM, not the stored string. 0008 stores the enum
' NAME under COLLATE utf8mb4_bin and a CHECK constraint, so the only values
' that can reach this property are the seven PurchaseOrderStatus names; the
' repository's parse is therefore a total function, and a value that somehow
' were not a name would throw at the read rather than travel onward as an
' unrecognised string. That is the intended failure direction (ADR-020).
'
' SupplierName is a PROJECTION, not a column. It is populated by the
' repository's join against Suppliers on read, and is left empty on an
' instance built for an insert - the insert never writes it. It lives here
' rather than in a separate read model because every read of an order needs
' it and no write does; see PurchaseOrderLine.ProductSku/ProductName for the
' same arrangement, and PurchaseOrderLineResponse's header for why the name
' is joined live rather than captured on the row.
'
' IMPLEMENTS IOwnershipResource (P3-04, ADR-017 section 6). RequestedByUserId
' below is also the interface's property - PurchaseOrdersController loads an
' order and passes it straight to
' IAuthorizationService.AuthorizeAsync(User, order, PurchaseOrders.Approve),
' and SelfApprovalHandler compares the authenticated actor against this same
' property. Nothing extra to wire: the property already existed for P3-03's
' response mapping.

Imports System.Collections.Generic
Imports Merchandising.Domain.Procurement
Imports Merchandising.Domain.Security

Namespace Entities

    ''' <summary>
    ''' A single PurchaseOrders row, with its lines when read through
    ''' <c>PurchaseOrderRepository.GetByIdAsync</c>.
    ''' </summary>
    Public NotInheritable Class PurchaseOrder
        Implements IOwnershipResource

        Public Property Id As Integer

        ''' <summary>Server-generated and unique (UQ_PurchaseOrders_OrderNumber). Never client-supplied.</summary>
        Public Property OrderNumber As String = String.Empty

        Public Property SupplierId As Integer

        ''' <summary>Joined from Suppliers on read; empty on an instance built for an insert.</summary>
        Public Property SupplierName As String = String.Empty

        Public Property Status As PurchaseOrderStatus

        Public Property RequestedByUserId As Integer Implements IOwnershipResource.RequestedByUserId

        ''' <summary>Null until an approval endpoint sets it. Kept distinct from <see cref="RequestedByUserId"/> so the self-approval rule has both to compare (ADR-017 section 6).</summary>
        Public Property ApprovedByUserId As Integer?

        Public Property SubmittedAtUtc As DateTime?

        Public Property ApprovedAtUtc As DateTime?

        Public Property RowVersion As Long

        Public Property CreatedAtUtc As DateTime

        Public Property UpdatedAtUtc As DateTime

        ''' <summary>The order's lines, ordered by LineNumber. Empty on a summary read, which deliberately does not fetch them.</summary>
        Public Property Lines As IReadOnlyList(Of PurchaseOrderLine) = Array.Empty(Of PurchaseOrderLine)()

        ''' <summary>Line count for a summary read, where <see cref="Lines"/> is deliberately not populated.</summary>
        Public Property LineCount As Integer

    End Class

End Namespace
