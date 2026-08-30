' Merchandising.Contracts.Reporting.ReportRangeEnvelope
'
' P6-01: spec section 14 - "All reports must ... show the selected date
' range, and indicate whether returns/cancellations are included or
' excluded." PurchaseOrderHistoryResponse (P3-06) already echoes
' FromDate/ToDate/TimeZone for one report; this pulls that shape out so
' every one of Track B's twelve reports embeds the SAME four fields under
' the SAME names, rather than each card inventing its own echo block. A
' report response composes this alongside its own Items/aggregate
' properties - it is not a response on its own.
'
' FromDate/ToDate/TimeZone carry PurchaseOrderHistoryResponse's exact
' contract forward: what was ACTUALLY applied, never what was requested -
' null means unbounded on that side, not "today" or any other silent
' default. ReturnsTreatment is new: the Merchandising.Domain.Reporting.
' ReturnsTreatment value docs/report-specification.md section 4 assigns to
' THIS report, serialized as its enum name ("Included"/"Excluded") - this
' project's reflection-based JSON serialization has no enum converter
' (CLAUDE.md section 3), so every other enum-shaped wire field in this
' codebase (PurchaseOrderSummaryResponse.Status, for one) is already a
' String set by the controller from the enum's name, and this follows the
' same convention rather than introducing a second one.

Imports System.Text.Json.Serialization

Namespace Reporting

    Public NotInheritable Class ReportRangeEnvelope

        ''' <summary>The inclusive lower date bound actually applied, store-local (yyyy-MM-dd). Null when unbounded.</summary>
        <JsonPropertyName("fromDate")>
        Public Property FromDate As String = Nothing

        ''' <summary>The inclusive upper date bound actually applied, store-local (yyyy-MM-dd). Null when unbounded.</summary>
        <JsonPropertyName("toDate")>
        Public Property ToDate As String = Nothing

        ''' <summary>The IANA id the date range was interpreted against - Merchandising.Domain.StoreTimeZone.IanaId, always "Asia/Manila".</summary>
        <JsonPropertyName("timeZone")>
        Public Property TimeZone As String = String.Empty

        ''' <summary>"Included" or "Excluded" - Merchandising.Domain.Reporting.ReturnsTreatment's name for THIS report, per docs/report-specification.md section 4. Never left for the caller to infer.</summary>
        <JsonPropertyName("returnsTreatment")>
        Public Property ReturnsTreatment As String = String.Empty

    End Class

End Namespace
