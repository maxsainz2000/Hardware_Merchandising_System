' Merchandising.Domain.Reporting.ReturnsTreatment
'
' P6-01: spec section 14 requires every report to "indicate whether
' returns/cancellations are included or excluded." docs/report-specification.md
' section 4 assigns exactly one of these two values to each of the twelve
' reports, and a report response echoes that value the same way
' PurchaseOrderHistoryResponse already echoes the date range actually applied
' (FromDate/ToDate/TimeZone) - a caller must never have to guess or re-derive
' which treatment the server used.
'
' Two values only. This project has no report that partially nets returns -
' one that needs a third state (for example, "shown separately but not
' netted") is a new decision for whichever card first needs it, not a value
' squeezed in here to save a future conversation.
'
' Pure enumeration, no I/O. Domain depends on nothing (CLAUDE.md section 4).

Namespace Reporting

    ''' <summary>
    ''' Whether a report's totals net returns/cancellations into the figure
    ''' shown, or leave them out entirely. See
    ''' docs/report-specification.md section 4 for the value assigned to each
    ''' of spec section 14's twelve reports and why.
    ''' </summary>
    Public Enum ReturnsTreatment

        ''' <summary>Returns/cancellations are netted into the reported total - a return reduces it, a completed sale increases it.</summary>
        Included

        ''' <summary>The report's figures do not reflect returns/cancellations at all - either because the report's subject has no returns dimension, or because returns are reported elsewhere by design.</summary>
        Excluded

    End Enum

End Namespace
