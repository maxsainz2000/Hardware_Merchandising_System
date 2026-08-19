' Merchandising.Tests.Integration.MerchandisingApiFactory
'
' P1-19: the WebApplicationFactory seam. Everything written before this card
' tested the API by calling AuthService and StockService directly, which means
' nothing exercised the HTTP pipeline itself - routing, model binding, the
' authentication handler, the middleware order, or the error envelope.
'
' That gap was not theoretical. P1-10 found a live defect that existed ONLY at
' that layer: an unauthenticated request could return HTTP 500 as text/plain
' carrying a MySqlException, a 25-frame stack trace and absolute source paths.
' No service-level test could have seen it, because the leak happened in
' middleware that service-level tests never run.
'
' On the type argument: it is ApiEntryPoint, not Program, because a Visual
' Basic Module cannot be used as a type (BC30371). See ApiEntryPoint.vb - the
' gap and the confirmation are recorded there.
'
' The host runs under the "Testing" environment, which is the one thing
' Program.Main checks: it skips the Kestrel/certificate configuration, since
' TestServer replaces Kestrel entirely and there is no .pfx to load. Every
' other line of startup - DI registration, authentication, middleware order,
' controllers - runs exactly as it does in production. The database is the
' real pinned MariaDB, per ADR-000; nothing here substitutes it.

Imports Merchandising.Api
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Mvc.Testing
Imports Microsoft.Extensions.Hosting

''' <summary>Hosts the real API in-memory for HTTP-level tests.</summary>
Friend NotInheritable Class MerchandisingApiFactory
    Inherits WebApplicationFactory(Of ApiEntryPoint)

    Protected Overrides Sub ConfigureWebHost(builder As IWebHostBuilder)

        builder.UseEnvironment(Program.TestingEnvironmentName)

    End Sub

End Class
