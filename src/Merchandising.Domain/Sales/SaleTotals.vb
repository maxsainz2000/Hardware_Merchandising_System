' Merchandising.Domain.Sales.SaleTotals
'
' P5-01 / ADR-004.1: "Totals are derived from already-rounded components,
' never rounded independently of them." Each SaleLine.LineTotal is already
' rounded to DECIMAL(19,4) at construction, so the sale total is a plain
' sum with no second rounding step - header and lines are equal by
' construction, not by coincidence.

Imports System.Collections.Generic

Namespace Sales

    Public NotInheritable Class SaleTotals

        Private Sub New()
        End Sub

        ''' <summary>Sum of already-rounded SaleLine.LineTotal values. Never re-rounds the sum.</summary>
        Public Shared Function ComputeSaleTotal(lines As IEnumerable(Of SaleLine)) As Decimal

            If lines Is Nothing Then
                Throw New ArgumentNullException(NameOf(lines))
            End If

            Dim total As Decimal = 0D

            For Each line In lines
                total += line.LineTotal
            Next

            Return total

        End Function

    End Class

End Namespace
