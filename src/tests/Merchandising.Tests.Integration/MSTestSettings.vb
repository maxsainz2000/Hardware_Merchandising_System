Imports Microsoft.VisualStudio.TestTools.UnitTesting

' Integration tests share ONE real MariaDB instance (ADR-000: no in-memory
' substitute is permitted). The MSTest template's default here is
' <Assembly: Parallelize(Scope:=ExecutionScope.MethodLevel)>, which would run
' them concurrently against that shared state.
'
' That default is wrong for this suite and was replaced at P1-01 per
' ADR-009.2. Left in place it would make P1-12's rollback proof and P1-14's
' idempotency proof intermittently fail for reasons unrelated to the code
' under test - and an intermittent failure in exactly those two proofs is the
' worst possible place to spend debugging time, because both are meant to be
' evidence that the transaction design is sound.
'
' P1-13 proves concurrency by creating its own inside a single test. It does
' not need, and must not get, additional concurrency from the test runner.
'
' Do not restore Parallelize here. If a future suite genuinely needs
' parallelism, isolate it per test class rather than re-enabling it globally.
<Assembly: DoNotParallelize>
