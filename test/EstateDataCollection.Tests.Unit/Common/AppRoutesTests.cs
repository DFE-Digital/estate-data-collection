using AwesomeAssertions;
using EstateDataCollection.Common.Routing;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Common;

public sealed class AppRoutesTests
{
    [Fact]
    public void AdminUi_Builders_Should_Format_Escaped_Paths_And_Optional_Queries()
    {
        var submissionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        AppRoutes.AdminUi.Dashboard.Should().Be("/manage");
        AppRoutes.AdminUi.Statistics.Should().Be("/manage/statistics");
        AppRoutes.AdminUi.Submissions.Should().Be("/manage/submissions");
        AppRoutes.AdminUi.SubmissionDetail(submissionId).Should().Be($"/manage/submissions/{submissionId}");

        AppRoutes.AdminUi.SubmissionsFiltered().Should().Be("/manage/submissions");
        AppRoutes.AdminUi.SubmissionsFiltered(
                schemaName: "cdc survey",
                version: 2,
                status: "Draft",
                query: "oak & pine")
            .Should()
            .Be("/manage/submissions?schemaName=cdc%20survey&version=2&status=Draft&q=oak%20%26%20pine");

        AppRoutes.AdminUi.SchemaVersions("school site").Should().Be("/manage/schemas/school%20site");
        AppRoutes.AdminUi.SchemaDetail("school site", 3).Should().Be("/manage/schemas/school%20site/3/details");
        AppRoutes.AdminUi.SchemaDetail("school site", 3, "saved ok").Should().Be("/manage/schemas/school%20site/3/details?notice=saved%20ok");
        AppRoutes.AdminUi.SchemaTests("school site", 3).Should().Be("/manage/schemas/school%20site/3/tests");
        AppRoutes.AdminUi.SchemaTests("school site", 3, "updated").Should().Be("/manage/schemas/school%20site/3/tests?notice=updated");
        AppRoutes.AdminUi.SchemaTestsRun("school site", 3).Should().Be("/manage/schemas/school%20site/3/tests/run");
        AppRoutes.AdminUi.SchemaSchedule("school site", 3).Should().Be("/manage/schemas/school%20site/3/schedule");
    }

    [Fact]
    public void PublicApi_And_AdminApi_Builders_Should_Prepend_Root_Prefix_And_Escape_Segments()
    {
        var submissionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        AppRoutes.AdminApi.Schemas.Should().Be("/api/schemas");
        AppRoutes.AdminApi.SchemaVersionDetail("edc payload", 1).Should().Be("/api/schemas/edc%20payload/versions/1");
        AppRoutes.AdminApi.ScheduleSchema("edc payload", 1).Should().Be("/api/schemas/edc%20payload/versions/1/schedule");
        AppRoutes.AdminApi.UpdateSchemaTests("edc payload", 1).Should().Be("/api/schemas/edc%20payload/versions/1/tests");

        AppRoutes.PublicApi.About.Should().Be("/api/about");
        AppRoutes.PublicApi.Schemas.Should().Be("/api/schemas");
        AppRoutes.PublicApi.CreateSubmissionTemplate.Should().Be("/api/submissions/{schemaName}");
        AppRoutes.PublicApi.SchemaByName("edc payload").Should().Be("/api/schemas/edc%20payload");
        AppRoutes.PublicApi.SchemaVersionDetail("edc payload", 2).Should().Be("/api/schemas/edc%20payload/versions/2");
        AppRoutes.PublicApi.CreateSubmission("edc payload").Should().Be("/api/submissions/edc%20payload");
        AppRoutes.PublicApi.CreateSubmission("edc payload", 2).Should().Be("/api/submissions/edc%20payload?version=2");
        AppRoutes.PublicApi.SubmissionStatus(submissionId).Should().Be($"/api/submissions/{submissionId}");
        AppRoutes.PublicApi.UpdateSubmission(submissionId).Should().Be($"/api/submissions/{submissionId}");
        AppRoutes.PublicApi.CheckSubmission(submissionId).Should().Be($"/api/submissions/{submissionId}/check");
        AppRoutes.PublicApi.FinaliseSubmission(submissionId).Should().Be($"/api/submissions/{submissionId}/finalise");
    }

    [Fact]
    public void Assets_Paths_Should_Format_Clean_Asset_Urls_From_Templates()
    {
        AppRoutes.Assets.FaviconIco.Should().Be("/assets/images/favicon.ico");
        AppRoutes.Assets.FaviconSvg.Should().Be("/assets/images/favicon.svg");
        AppRoutes.Assets.GovUkCss.Should().Be("/assets/css/govuk-frontend.min.css");
        AppRoutes.Assets.DfeBrandingCss.Should().Be("/assets/css/dfe-branding.css");
        AppRoutes.Assets.GovUkJs.Should().Be("/assets/js/govuk-frontend.min.js");
        AppRoutes.Assets.WorkbenchJs.Should().Be("/assets/js/workbench.js");
    }
}
