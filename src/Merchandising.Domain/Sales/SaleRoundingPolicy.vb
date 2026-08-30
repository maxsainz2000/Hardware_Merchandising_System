' Merchandising.Domain.Sales.SaleRoundingPolicy
'
' P5-01 / ADR-022: the "currency.roundingPolicy" SystemSettings value
' (Merchandising.Domain.Configuration.SystemSettingRegistry.Keys.
' CurrencyRoundingPolicy, registered at P2-05 with no consumer until now)
' is stored as one of two strings. Sale arithmetic must round using
' whichever one is currently configured, never an implicit default - this
' is the one place that string is turned into the MidpointRounding the
' .NET Decimal.Round overloads actually take.
'
' Domain "depends on nothing" (CLAUDE.md section 4), so this class never
' reads SystemSettingsRepository itself. The caller (Infrastructure/Api,
' a later card) reads the setting row and passes the resolved
' MidpointRounding into SaleLine/CashTender - the same shape
' AdjustmentThresholdPolicy takes its threshold as a parameter rather
' than reading SystemSettings itself.

Imports Merchandising.Domain.Configuration

Namespace Sales

    Public NotInheritable Class SaleRoundingPolicy

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Resolves a "currency.roundingPolicy" setting value to the
        ''' MidpointRounding it names. Throws rather than silently
        ''' defaulting - an unrecognized value is a configuration defect,
        ''' not something sale arithmetic should guess about.
        ''' </summary>
        Public Shared Function Parse(settingValue As String) As MidpointRounding

            If String.Equals(settingValue, SystemSettingRegistry.RoundingPolicyNames.AwayFromZero, StringComparison.Ordinal) Then
                Return MidpointRounding.AwayFromZero
            End If

            If String.Equals(settingValue, SystemSettingRegistry.RoundingPolicyNames.ToEven, StringComparison.Ordinal) Then
                Return MidpointRounding.ToEven
            End If

            Throw New ArgumentException(
                $"'{settingValue}' is not a recognized currency.roundingPolicy value. " &
                $"Expected '{SystemSettingRegistry.RoundingPolicyNames.AwayFromZero}' or '{SystemSettingRegistry.RoundingPolicyNames.ToEven}'.",
                NameOf(settingValue))

        End Function

    End Class

End Namespace
