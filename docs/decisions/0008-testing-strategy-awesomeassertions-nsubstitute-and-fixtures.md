---
status: "proposed"
sub-status: "implemented"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 8. Testing Strategy with NSubstitute, AwesomeAssertions, and Reusable Fixtures

## Context and Problem Statement

A high-throughput ingestion platform handling statutory education condition data requires an automated testing strategy 
that is fast, expressive, easy to maintain, and clearly segregated between fast in-memory business logic and external 
integration boundaries.

Without a disciplined approach, test suites suffer from:
1. Brittle, copy-pasted mock setups and duplicated test payload fixtures.
2. Inexpressive assertions that produce cryptic failure messages in CI/CD pipelines.
3. Heavy mock libraries that slow test execution or introduce license changes.
4. Conflation of fast unit tests with slower, network/disk-dependent integration tests.

How should automated testing be structured across the solution to ensure high maintainability, clear behavioural 
specifications, and reusable test fixtures?

## Decision Drivers

* **Expressive & Readable Assertions:** Clear failure diagnostics and fluent assertion syntax.
* **Modern, Lightweight Mocking:** Clean, type-safe substitute creation with straightforward call verification without 
  lambda verbosity.
* **DRY Test Data Creation:** Reusable immutable builders and golden test fixtures shared across Unit and Integration 
  test projects.
* **Separation of Test Scopes:** Strict partitioning between fast in-memory unit tests (<400ms) and containerised 
  integration tests.

## Considered Options

* **Option 1: NSubstitute + AwesomeAssertions + Shared `Tests.Common` Fixture Library**
* **Option 2: Moq + FluentAssertions**
* **Option 3: Standard xUnit `Assert.*` with Hand-Written Test Stubs**

## Decision Outcome

Chosen option: **"Option 1: NSubstitute + AwesomeAssertions + Shared `Tests.Common` Fixture Library"**, because 
`AwesomeAssertions` provides modern, fluent assertion syntax with rich diagnostics, `NSubstitute` offers a clean 
lambda-free mocking API, and placing shared builders in `EstateDataCollection.Tests.Common` promotes reuse across both 
test suites:

1. **Test Project Segregation**:
   - `EstateDataCollection.Tests.Common`: Shared test data builders, golden JSON Schema fixtures, Bogus data generators, 
     and test doubles. No tests run directly in this project.
   - `EstateDataCollection.Tests.Unit`: Fast, isolated, in-memory tests running without network or file system dependencies 
     (targeting Domain, Application Use Cases, and Infrastructure logic). Executes the full suite in under 400ms.
   - `EstateDataCollection.Tests.Integration`: End-to-end integration tests executing against local containerised Azurite 
     storage emulators and test host instances.
2. **Assertion and Mocking Standards**:
   - **`AwesomeAssertions`**: Used for all test assertions (`result.IsSuccess.Should().BeTrue()`, 
     `envelope.Status.Should().Be(SubmissionStatus.Draft)`). Provides readable, fluent syntax and rich diagnostic output 
     upon failure.
   - **`NSubstitute`**: Used for interface test doubles (`Substitute.For<IBlobService>()`, 
     `substitute.Received(1).WriteAsync(...)`) providing concise, lambda-free syntax without runtime generation overhead.
3. **Builder Pattern & Golden Fixtures**:
   Domain records and entities are constructed using immutable fluent builders (e.g., `SchemaMetadataBuilder`) with 
   sensible production defaults. Golden fixtures (`JsonSchemaFixtures`, `SurveyPayloadFixtures`) and deterministic random 
   generators (`SchoolPayloadGenerator` via Bogus) prevent test payload duplication.
4. **Behavioural Test Specification Naming**:
   Test methods adhere strictly to the behavioural naming convention:
   `When_<ConditionOrAction>_It_Should_<ExpectedOutcome>`
   (e.g., `When_Finalising_With_Valid_Payload_It_Should_Promote_To_Processed_Container`).

### Consequences

* Good, because tests read like executable behavioural specifications.
* Good, because test doubles for storage, use cases, and repositories are trivial to configure and verify.
* Good, because test setup boilerplate is minimised by domain builders with sensible default values.
* Good, because the entire unit test suite runs in under 400 milliseconds, giving developers instant feedback.
* Bad, because developers must maintain test builders as new domain records are introduced.

### Confirmation

Compliance is confirmed via:
- Automated execution of all unit and integration tests in CI/CD via `dotnet test`.
- Consistent usage of `AwesomeAssertions` (`.Should().Be(...)`) across all test projects.
- Shared builders in `EstateDataCollection.Tests.Common` utilised across tests.

## Pros and Cons of the Options

### Option 1: NSubstitute + AwesomeAssertions + Common Fixtures (Chosen)

* Good, because tests are concise, readable, and highly expressive.
* Good, because NSubstitute syntax is clean and avoids fragile lambda mocking.
* Good, because test builders eliminate copy-pasted payload setups.
* Bad, because third-party testing libraries must be kept updated.

### Option 2: Moq + FluentAssertions

* Good, because historically familiar to many .NET developers.
* Bad, because Moq has had licensing controversies and verbose `.Setup(x => x.Method(It.IsAny<T>()))` syntax.
* Bad, because FluentAssertions introduced commercial licensing changes in newer versions.

### Option 3: Standard xUnit Assert.* with Hand-Written Stubs

* Good, because zero third-party dependencies are required.
* Bad, because assertion failure messages for complex objects are unhelpful and verbose.
* Bad, because hand-written stubs require substantial maintenance overhead.

## More Information

Project Structure:
* `test/EstateDataCollection.Tests.Common`: Builders, Fixtures, and Bogus Generators.
* `test/EstateDataCollection.Tests.Unit`: Fast in-memory unit tests.
* `test/EstateDataCollection.Tests.Integration`: Azurite and end-to-end integration tests.
