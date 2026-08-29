' Merchandising.Domain.Configuration.SystemSettingRegistry
'
' P2-05 / spec section 12: "The system stores a configurable currency code
' and uses one defined rounding policy." Promotes those two facts from
' constants to real, typed, validated SystemSettings entries.
'
' SCOPED TO EXACTLY THE TWO SETTINGS SECTION 12 NAMES, deliberately. The
' pre-existing "backup." and "maintenance." keys (P1-17, P1-18) already work
' through their own direct SystemSettingsRepository.LoadByPrefixAsync
' readers and are not retrofitted into this registry - CLAUDE.md's "don't
' design for hypothetical future requirements" applies to widening this
' registry's scope just as much as to widening a role's permissions. A
' future card that needs another administered setting adds its own
' Definitions entry, the same way PolicyRegistry.Definitions grows one
' operation at a time.
'
' P4-10 adds exactly that third entry: "inventory.adjustmentThreshold",
' spec section 10.2's "the configured threshold" for stock-adjustment
' approval ("The threshold lives in SystemSettings, not in a constant" -
' the card's own "Do" text). AdjustmentService reads it directly through
' SystemSettingsRepository (never through this controller's GET/PUT), but
' registering it here means it is also visible and administrable through
' the existing GET/PUT /api/v1/admin/settings surface for free - the same
' mechanism, not a new one.
'
' Pure data plus validation, no ASP.NET Core or database dependency -
' Domain "depends on nothing" (CLAUDE.md section 4), the same shape as
' Merchandising.Domain.Security.PolicyRegistry.

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq

Namespace Configuration

    ''' <summary>One administered setting: its key, default, and validator.</summary>
    Public NotInheritable Class SystemSettingDefinition

        Public ReadOnly Property Key As String
        Public ReadOnly Property DefaultValue As String
        Public ReadOnly Property Description As String

        Private ReadOnly _validate As Func(Of String, String)

        Public Sub New(settingKey As String, settingDefault As String, validator As Func(Of String, String), settingDescription As String)

            Key = settingKey
            DefaultValue = settingDefault
            Description = settingDescription
            _validate = validator

        End Sub

        ''' <summary>Nothing if <paramref name="value"/> is valid; otherwise a human-readable validation message.</summary>
        Public Function Validate(value As String) As String
            Return _validate(value)
        End Function

    End Class

    Public NotInheritable Class SystemSettingRegistry

        ''' <summary>Every registered setting key, so callers never spell one as a string literal.</summary>
        Public NotInheritable Class Keys
            Public Const CurrencyCode As String = "currency.code"
            Public Const CurrencyRoundingPolicy As String = "currency.roundingPolicy"
            Public Const AdjustmentApprovalThreshold As String = "inventory.adjustmentThreshold"
        End Class

        ''' <summary>The two rounding policy names this system recognizes - the two <see cref="MidpointRounding"/> members .NET actually offers.</summary>
        Public NotInheritable Class RoundingPolicyNames
            Public Const AwayFromZero As String = "AwayFromZero"
            Public Const ToEven As String = "ToEven"
        End Class

        Public Shared ReadOnly Property Definitions As IReadOnlyList(Of SystemSettingDefinition) = BuildDefinitions()

        ''' <summary>Nothing if <paramref name="key"/> is not a registered setting.</summary>
        Public Shared Function Find(key As String) As SystemSettingDefinition
            Return Definitions.FirstOrDefault(Function(definition) String.Equals(definition.Key, key, StringComparison.Ordinal))
        End Function

        Private Shared Function BuildDefinitions() As IReadOnlyList(Of SystemSettingDefinition)

            Dim collected As New List(Of SystemSettingDefinition)

            collected.Add(New SystemSettingDefinition(
                Keys.CurrencyCode,
                "PHP",
                AddressOf ValidateCurrencyCode,
                "Three-letter ISO 4217-shaped currency code, for example PHP."))

            collected.Add(New SystemSettingDefinition(
                Keys.CurrencyRoundingPolicy,
                RoundingPolicyNames.AwayFromZero,
                AddressOf ValidateRoundingPolicy,
                $"One of '{RoundingPolicyNames.AwayFromZero}' or '{RoundingPolicyNames.ToEven}'."))

            collected.Add(New SystemSettingDefinition(
                Keys.AdjustmentApprovalThreshold,
                "10.000",
                AddressOf ValidateAdjustmentApprovalThreshold,
                "A non-negative quantity, DECIMAL(19,3) scale. A stock adjustment whose |variance| is at or above this value requires a second person's approval."))

            Return collected.AsReadOnly()

        End Function

        Private Shared Function ValidateCurrencyCode(value As String) As String

            If String.IsNullOrEmpty(value) OrElse value.Length <> 3 OrElse Not value.All(AddressOf IsAsciiUpperLetter) Then
                Return "Currency code must be exactly three uppercase letters, for example PHP."
            End If

            Return Nothing

        End Function

        Private Shared Function ValidateRoundingPolicy(value As String) As String

            If Not String.Equals(value, RoundingPolicyNames.AwayFromZero, StringComparison.Ordinal) AndAlso
               Not String.Equals(value, RoundingPolicyNames.ToEven, StringComparison.Ordinal) Then

                Return $"Rounding policy must be '{RoundingPolicyNames.AwayFromZero}' or '{RoundingPolicyNames.ToEven}'."

            End If

            Return Nothing

        End Function

        Private Shared Function IsAsciiUpperLetter(character As Char) As Boolean
            Return character >= "A"c AndAlso character <= "Z"c
        End Function

        ''' <summary>ADR-004.1: a stored setting value is text, so its own scale must be validated the same way an API-boundary quantity is - never trusted just because it parses.</summary>
        Private Shared Function ValidateAdjustmentApprovalThreshold(value As String) As String

            Dim parsed As Decimal

            If Not Decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, parsed) Then
                Return "The adjustment approval threshold must be a decimal number."
            End If

            If parsed < 0D Then
                Return "The adjustment approval threshold cannot be negative."
            End If

            If Not DecimalScaleGuard.IsAtQuantityScale(parsed) Then
                Return $"The adjustment approval threshold must have no more than {DecimalScaleGuard.QuantityScale} decimal places."
            End If

            Return Nothing

        End Function

    End Class

End Namespace
