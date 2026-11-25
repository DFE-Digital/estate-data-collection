using AwesomeAssertions;
using EstateDataCollection.Common.Routing;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Common;

public sealed class AppRoutesTests
{
    [Fact]
    public void AdminUi_Builders_Should_Format_Escaped_Paths_And_Optional_Queries()
    {
        // Arrange
        var submissionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        // Act
        var submissionDetail = AppRoutes.AdminUi.SubmissionDetail(submissionId);
        var filteredDefault = AppRoutes.AdminUi.SubmissionsFiltered();
        var filteredFull = AppRoutes.AdminUi.SubmissionsFiltered(
            schemaName: "cdc survey",
            version: 2,
            status: "Draft",
            query: "oak & pine");
        var schemaVersions = AppRoutes.AdminUi.SchemaVersions("school site");
        var schemaDetail = AppRoutes.AdminUi.SchemaDetail("school site", 3);
        var schemaDetailNotice = AppRoutes.AdminUi.SchemaDetail("school site", 3, "saved ok");
        var schemaTests = AppRoutes.AdminUi.SchemaTests("school site", 3);
        var schemaTestsNotice = AppRoutes.AdminUi.SchemaTests("school site", 3, "updated");
        var schemaTestsRun = AppRoutes.AdminUi.SchemaTestsRun("school site", 3);
        var schemaSchedule = AppRoutes.AdminUi.SchemaSchedule("school site", 3);

        // Assert
        AppRoutes.AdminUi.Dashboard.Should().Be("/manage");
        AppRoutes.AdminUi.Statistics.Should().Be("/manage/statistics");
        AppRoutes.AdminUi.Submissions.Should().Be("/manage/submissions");
        submissionDetail.Should().Be($"/manage/submissions/{submissionId}");
        filteredDefault.Should().Be("/manage/submissions");
        filteredFull.Should().Be("/manage/submissions?schemaName=cdc%20survey&version=2&status=Draft&q=oak%20%26%20pine");
        schemaVersions.Should().Be("/manage/schemas/school%20site");
        schemaDetail.Should().Be("/manage/schemas/school%20site/3/details");
        schemaDetailNotice.Should().Be("/manage/schemas/school%20site/3/details?notice=saved%20ok");
        schemaTests.Should().Be("/manage/schemas/school%20site/3/tests");
        schemaTestsNotice.Should().Be("/manage/schemas/school%20site/3/tests?notice=updated");
        schemaTestsRun.Should().Be("/manage/schemas/school%20site/3/tests/run");
        schemaSchedule.Should().Be("/manage/schemas/school%20site/3/schedule");
    }

    [Fact]
    public void AdminApi_Builders_Should_Prepend_Root_Prefix_And_Escape_Segments()
    {
        // Arrange — AppRoutes.AdminApi route builders

        // Act
        var schemas = AppRoutes.AdminApi.Schemas;
        var detail = AppRoutes.AdminApi.SchemaVersionDetail("edc payload", 1);
        var schedule = AppRoutes.AdminApi.ScheduleSchema("edc payload", 1);
        var updateTests = AppRoutes.AdminApi.UpdateSchemaTests("edc payload", 1);

        // Assert
        schemas.Should().Be("/api/schemas");
        detail.Should().Be("/api/schemas/edc%20payload/versions/1");
        schedule.Should().Be("/api/schemas/edc%20payload/versions/1/schedule");
        updateTests.Should().Be("/api/schemas/edc%20payload/versions/1/tests");
    }

    [Fact]
    public void PublicApi_Builders_Should_Prepend_Root_Prefix_And_Escape_Segments()
    {
        // Arrange
        var submissionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        // Act
        var schemaByName = AppRoutes.PublicApi.SchemaByName("edc payload");
        var schemaVersion = AppRoutes.PublicApi.SchemaVersionDetail("edc payload", 2);
        var createSubmission = AppRoutes.PublicApi.CreateSubmission("edc payload");
        var createSubmissionVersioned = AppRoutes.PublicApi.CreateSubmission("edc payload", 2);
        var status = AppRoutes.PublicApi.SubmissionStatus(submissionId);
        var update = AppRoutes.PublicApi.UpdateSubmission(submissionId);
        var check = AppRoutes.PublicApi.CheckSubmission(submissionId);
        var finalise = AppRoutes.PublicApi.FinaliseSubmission(submissionId);

        // Assert
        AppRoutes.PublicApi.About.Should().Be("/api/about");
        AppRoutes.PublicApi.Schemas.Should().Be("/api/schemas");
        AppRoutes.PublicApi.CreateSubmissionTemplate.Should().Be("/api/submissions/{schemaName}");
        schemaByName.Should().Be("/api/schemas/edc%20payload");
        schemaVersion.Should().Be("/api/schemas/edc%20payload/versions/2");
        createSubmission.Should().Be("/api/submissions/edc%20payload");
        createSubmissionVersioned.Should().Be("/api/submissions/edc%20payload?version=2");
        status.Should().Be($"/api/submissions/{submissionId}");
        update.Should().Be($"/api/submissions/{submissionId}");
        check.Should().Be($"/api/submissions/{submissionId}/check");
        finalise.Should().Be($"/api/submissions/{submissionId}/finalise");
    }

    [Fact]
    public void Assets_Paths_Should_Format_Clean_Asset_Urls_From_Templates()
    {
        // Arrange — AppRoutes.Assets static URLs

        // Act
        var faviconIco = AppRoutes.Assets.FaviconIco;
        var faviconSvg = AppRoutes.Assets.FaviconSvg;
        var govUkCss = AppRoutes.Assets.GovUkCss;
        var dfeCss = AppRoutes.Assets.DfeBrandingCss;
        var govUkJs = AppRoutes.Assets.GovUkJs;
        var workbenchJs = AppRoutes.Assets.WorkbenchJs;

        // Assert
        faviconIco.Should().Be("/assets/images/favicon.ico");
        faviconSvg.Should().Be("/assets/images/favicon.svg");
        govUkCss.Should().Be("/assets/css/govuk-frontend.min.css");
        dfeCss.Should().Be("/assets/css/dfe-branding.css");
        govUkJs.Should().Be("/assets/js/govuk-frontend.min.js");
        workbenchJs.Should().Be("/assets/js/workbench.js");
    }
}
