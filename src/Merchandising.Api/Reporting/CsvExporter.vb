' Merchandising.Api.Reporting.CsvExporter
'
' P6-06: generic CSV mechanics shared by all twelve report export routes -
' spec section 14's own paragraph: "CSV exports use UTF-8, include a header
' row, use invariant field ordering, escape delimiters/quotes correctly, and
' include report parameters in the export metadata or filename." This class
' owns exactly that - RFC4180 field escaping, the formula-injection
' mitigation, UTF-8-with-BOM encoding, CRLF line endings, and the shared
' filename shape. It owns NO report-specific column list - each report's own
' header/row arrays live in ReportCsvFormatters, one file over.
'
' FORMULA-INJECTION MITIGATION, MEASURED ON THIS MACHINE, NOT ASSUMED -
' docs/adr.md ADR-024. A field opened through Excel's normal CSV file-open
' path (Workbooks.Open - the same path a double-click uses) is evaluated as
' a live formula whenever its UNQUOTED content begins with '=', '+', or '-':
' confirmed by writing literal =1+1 / +1+1 / -1+1 fields and observing
' Range.HasFormula = True with the arithmetic actually evaluated (2, 2, 0)
' via COM automation, Office 16.0.20426.20000. A leading '@' did NOT trigger
' formula evaluation on this build, but is mitigated anyway - the card's own
' Done-when box names all four characters. Prefixing any of the four with a
' single leading apostrophe (') reliably produced Range.HasFormula = False
' for all four, confirmed the same way, whether or not the field was also
' RFC4180-quoted. The apostrophe stays VISIBLE in the cell's displayed text
' (Range.Text) - unlike the same character typed directly into a cell, where
' Excel hides it. That is the accepted trade-off, not a defect: a visible
' leading apostrophe on a would-be-formula value is the price of the value
' never executing.
'
' UTF-8 BOM - ALSO MEASURED. Two exports of an identical non-ASCII row
' ("José Muñoz Ñañez café"), differing only in a
' leading UTF-8 BOM, both opened correctly through Workbooks.Open on this
' machine's Excel build - Office 16.0.20426.20000 auto-detects BOM-less
' UTF-8 CSVs. The BOM is included anyway: zero cost on a build where
' auto-detection already works, and the widely-documented older-Excel
' failure mode it exists to guard against (a BOM-less UTF-8 CSV misread
' against the system ANSI code page, mangling every non-ASCII character) is
' not something this project can assume the classroom machine is free of.
' See evidence/phase-6/p6-06-excel-roundtrip/.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text

Namespace Reporting

    Public NotInheritable Class CsvExporter

        Public Const ContentType As String = "text/csv"

        ''' <summary>The four characters spec's own Done-when box names - CLAUDE.md's formula-injection row. See this class's header for what was actually measured.</summary>
        Private Shared ReadOnly FormulaInjectionLeadChars As Char() = {"="c, "+"c, "-"c, "@"c}

        Private Shared ReadOnly FieldsRequiringQuoting As Char() = {","c, """"c, ControlChars.Cr, ControlChars.Lf}

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Builds a complete CSV file's bytes: a UTF-8 BOM (this class's
        ''' header), the header row, then one row per <paramref name="rows"/>
        ''' entry, CRLF-terminated throughout. Every field is escaped by
        ''' <see cref="EscapeField"/> - callers never pre-escape.
        ''' </summary>
        Public Shared Function BuildFile(
            headers As IReadOnlyList(Of String), rows As IEnumerable(Of IReadOnlyList(Of String))) As Byte()

            Dim builder As New StringBuilder()

            AppendRow(builder, headers)

            For Each row As IReadOnlyList(Of String) In rows
                AppendRow(builder, row)
            Next

            ' Encoding.GetBytes NEVER writes the preamble, regardless of
            ' encoderShouldEmitUTF8Identifier - that flag only takes effect
            ' through a StreamWriter, which this class does not use. The BOM
            ' this class's header measures and requires must be prepended
            ' explicitly, every time.
            Dim encoding As New UTF8Encoding(encoderShouldEmitUTF8Identifier:=True)
            Dim preamble As Byte() = encoding.GetPreamble()
            Dim content As Byte() = encoding.GetBytes(builder.ToString())

            Dim result(preamble.Length + content.Length - 1) As Byte
            Array.Copy(preamble, 0, result, 0, preamble.Length)
            Array.Copy(content, 0, result, preamble.Length, content.Length)

            Return result

        End Function

        ''' <summary>
        ''' Formats a <see cref="Decimal"/> for a CSV field using
        ''' <see cref="CultureInfo.InvariantCulture"/> and no format
        ''' specifier. <see cref="Decimal"/> already carries the scale it was
        ''' read from the database with (ADR-004/ADR-004.1), so this
        ''' reproduces the stored scale exactly - e.g. "0.0000" for money,
        ''' "0.000" for a quantity - without hard-coding a per-column decimal
        ''' place count, and without ever routing the value through
        ''' <see cref="Double"/>.
        ''' </summary>
        Public Shared Function FormatDecimal(value As Decimal) As String
            Return value.ToString(CultureInfo.InvariantCulture)
        End Function

        Public Shared Function FormatInteger(value As Integer) As String
            Return value.ToString(CultureInfo.InvariantCulture)
        End Function

        ''' <summary>Empty field for <see langword="Nothing"/> - never the literal text "Nothing" or a sentinel number.</summary>
        Public Shared Function FormatNullableInteger(value As Integer?) As String
            Return If(value.HasValue, value.Value.ToString(CultureInfo.InvariantCulture), String.Empty)
        End Function

        Public Shared Function FormatBoolean(value As Boolean) As String
            Return value.ToString(CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' Every timestamp this API stores is UTC (CLAUDE.md section 5) -
        ''' this makes that explicit in the export itself with a literal
        ''' trailing 'Z' rather than leaving a bare local-looking timestamp
        ''' for a spreadsheet to silently reinterpret.
        ''' </summary>
        Public Shared Function FormatUtcDateTime(value As DateTime) As String
            Return value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' RFC4180 escaping plus the formula-injection mitigation this
        ''' class's header measures: a field whose content begins with '=',
        ''' '+', '-', or '@' gets a single leading apostrophe prepended
        ''' before the ordinary quoting decision (comma/quote/CR/LF) is made.
        ''' <see langword="Nothing"/> becomes an empty field, never the
        ''' literal text "Nothing".
        ''' </summary>
        Public Shared Function EscapeField(value As String) As String

            Dim text As String = If(value, String.Empty)

            If text.Length > 0 AndAlso Array.IndexOf(FormulaInjectionLeadChars, text(0)) >= 0 Then
                text = "'" & text
            End If

            If text.IndexOfAny(FieldsRequiringQuoting) < 0 Then
                Return text
            End If

            Return """" & text.Replace("""", """""") & """"

        End Function

        ''' <summary>
        ''' "report parameters in the filename" (spec section 14). Slug plus
        ''' the effective store-local date range that was actually applied -
        ''' never what was requested, the same "actually applied" contract
        ''' <c>ReportRangeEnvelope</c> already carries for the JSON
        ''' response. <paramref name="extraSuffix"/> is an optional further
        ''' parameter a report carries beyond the date range (e.g. a filtered
        ''' productId, or includeInactive on the current-stock report).
        ''' </summary>
        Public Shared Function BuildFileName(
            reportSlug As String, fromDate As String, toDate As String, Optional extraSuffix As String = Nothing) As String

            Dim rangePart As String

            If String.IsNullOrEmpty(fromDate) AndAlso String.IsNullOrEmpty(toDate) Then
                rangePart = "all-dates"
            ElseIf String.IsNullOrEmpty(fromDate) Then
                rangePart = "to-" & toDate
            ElseIf String.IsNullOrEmpty(toDate) Then
                rangePart = "from-" & fromDate
            ElseIf String.Equals(fromDate, toDate, StringComparison.Ordinal) Then
                rangePart = fromDate
            Else
                rangePart = fromDate & "_to_" & toDate
            End If

            Dim fileName As String = reportSlug & "_" & rangePart

            If Not String.IsNullOrEmpty(extraSuffix) Then
                fileName &= "_" & extraSuffix
            End If

            Return fileName & ".csv"

        End Function

        Private Shared Sub AppendRow(builder As StringBuilder, fields As IReadOnlyList(Of String))

            For i As Integer = 0 To fields.Count - 1

                If i > 0 Then
                    builder.Append(","c)
                End If

                builder.Append(EscapeField(fields(i)))

            Next

            builder.Append(ControlChars.Cr).Append(ControlChars.Lf)

        End Sub

    End Class

End Namespace
