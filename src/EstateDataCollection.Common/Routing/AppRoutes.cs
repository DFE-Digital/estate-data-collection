using EstateDataCollection.Common.Extensions;

namespace EstateDataCollection.Common.Routing;

/// <summary>
/// Central route templates and URL builders shared across Azure Functions triggers.
/// Changing a prefix or route constant here updates endpoint wiring and generated links everywhere.
/// </summary>
/// <remarks>
/// Typically within a ASP.net application a Url or UrlHelper method would be used for building a URL from a page or
/// controller endpoint, we don't have that within a Functions world so require a central location to help manage routing.
/// </remarks>
public static class AppRoutes
{
    public static class AdminUi
    {
        public const string Prefix = "manage";
        public const string Root = $"/{Prefix}";

        public static class Templates
        {
            public const string Dashboard = Prefix;
            public const string Statistics = $"{Prefix}/statistics";
            public const string Submissions = $"{Prefix}/submissions";
            public const string SubmissionDetail = $"{Prefix}/submissions/{{submissionId:guid}}";
            public const string Workbench = $"{Prefix}/workbench";
            public const string WorkbenchVerify = $"{Prefix}/workbench/verify";
            public const string WorkbenchSubmit = $"{Prefix}/workbench/submit";
            public const string RunAllTests = $"{Prefix}/tests/run";
            public const string SchemaVersions = $"{Prefix}/schemas/{{schemaName}}";
            public const string SchemaDetail = $"{Prefix}/schemas/{{schemaName}}/{{version:int}}/details";
            public const string SchemaTests = $"{Prefix}/schemas/{{schemaName}}/{{version:int}}/tests";
            public const string SchemaTestsRun = $"{Prefix}/schemas/{{schemaName}}/{{version:int}}/tests/run";
            public const string SchemaSchedule = $"{Prefix}/schemas/{{schemaName}}/{{version:int}}/schedule";
        }

        public const string Dashboard = $"/{Templates.Dashboard}";
        public const string Statistics = $"/{Templates.Statistics}";
        public const string Submissions = $"/{Templates.Submissions}";
        public const string Workbench = $"/{Templates.Workbench}";
        public const string WorkbenchVerify = $"/{Templates.WorkbenchVerify}";
        public const string WorkbenchSubmit = $"/{Templates.WorkbenchSubmit}";
        public const string RunAllTests = $"/{Templates.RunAllTests}";

        public static string SubmissionsFiltered(
            string? schemaName = null,
            int? version = null,
            string? status = null,
            string? query = null)
        {
            var parameters = new List<string>();
            if (!string.IsNullOrWhiteSpace(schemaName))
            {
                parameters.Add($"schemaName={Uri.EscapeDataString(schemaName)}");
            }

            if (version.HasValue)
            {
                parameters.Add($"version={version.Value}");
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                parameters.Add($"status={Uri.EscapeDataString(status)}");
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                parameters.Add($"q={Uri.EscapeDataString(query)}");
            }

            return parameters.Count == 0
                ? Submissions
                : $"{Submissions}?{string.Join("&", parameters)}";
        }

        public static string SubmissionDetail(Guid submissionId)
        {
            return $"/{Templates.SubmissionDetail}".FormatWith(new { submissionId });
        }

        public static string SchemaVersions(string schemaName)
        {
            return $"/{Templates.SchemaVersions}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName)
            });
        }

        public static string SchemaDetail(string schemaName, int version, string? notice = null)
        {
            var basePath = $"/{Templates.SchemaDetail}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
            return string.IsNullOrWhiteSpace(notice)
                ? basePath
                : $"{basePath}?notice={Uri.EscapeDataString(notice)}";
        }

        public static string SchemaTests(string schemaName, int version, string? notice = null)
        {
            var basePath = $"/{Templates.SchemaTests}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
            return string.IsNullOrWhiteSpace(notice)
                ? basePath
                : $"{basePath}?notice={Uri.EscapeDataString(notice)}";
        }

        public static string SchemaTestsRun(string schemaName, int version)
        {
            return $"/{Templates.SchemaTestsRun}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
        }

        public static string SchemaSchedule(string schemaName, int version)
        {
            return $"/{Templates.SchemaSchedule}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
        }
    }

    public static class Assets
    {
        public const string Prefix = "assets";
        public const string Root = $"/{Prefix}";

        public static class Templates
        {
            public const string Asset = $"{Prefix}/{{*assetPath}}";
            public const string FaviconIco = "images/favicon.ico";
            public const string FaviconSvg = "images/favicon.svg";
            public const string GovUkCss = "css/govuk-frontend.min.css";
            public const string DfeBrandingCss = "css/dfe-branding.css";
            public const string GovUkJs = "js/govuk-frontend.min.js";
            public const string WorkbenchJs = "js/workbench.js";
        }

        public static string Path(string assetPath)
        {
            var cleanPath = assetPath.TrimStart('/');
            return $"/{Templates.Asset}".FormatWith(new { assetPath = cleanPath });
        }

        public static string FaviconIco => Path(Templates.FaviconIco);
        public static string FaviconSvg => Path(Templates.FaviconSvg);
        public static string GovUkCss => Path(Templates.GovUkCss);
        public static string DfeBrandingCss => Path(Templates.DfeBrandingCss);
        public static string GovUkJs => Path(Templates.GovUkJs);
        public static string WorkbenchJs => Path(Templates.WorkbenchJs);
    }

    public static class AdminApi
    {
        public const string Prefix = "api";
        public const string Root = $"/{Prefix}";

        public static class Templates
        {
            public const string Schemas = $"{Prefix}/schemas";
            public const string ValidationEngines = $"{Prefix}/schemas/engines";
            public const string VerifySchema = $"{Prefix}/schemas/verify";
            public const string RunSchemaTests = $"{Prefix}/schemas/tests/run";
            public const string VerifyLogic = $"{Prefix}/logic/verify";
            public const string SchemaVersionDetail = $"{Prefix}/schemas/{{schemaName}}/versions/{{version:int}}";
            public const string SubmitSchema = $"{Prefix}/schemas/{{schemaName}}/versions/{{version:int}}";
            public const string ScheduleSchema = $"{Prefix}/schemas/{{schemaName}}/versions/{{version:int}}/schedule";
            public const string UpdateSchemaTests = $"{Prefix}/schemas/{{schemaName}}/versions/{{version:int}}/tests";
        }

        public const string Schemas = $"/{Templates.Schemas}";
        public const string ValidationEngines = $"/{Templates.ValidationEngines}";
        public const string VerifySchema = $"/{Templates.VerifySchema}";
        public const string RunSchemaTests = $"/{Templates.RunSchemaTests}";
        public const string VerifyLogic = $"/{Templates.VerifyLogic}";

        public static string SchemaVersionDetail(string schemaName, int version)
        {
            return $"/{Templates.SchemaVersionDetail}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
        }

        public static string ScheduleSchema(string schemaName, int version)
        {
            return $"/{Templates.ScheduleSchema}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
        }

        public static string UpdateSchemaTests(string schemaName, int version)
        {
            return $"/{Templates.UpdateSchemaTests}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
        }
    }

    public static class PublicApi
    {
        /// <summary>
        /// Public Azure Functions host uses the default Azure Functions "api" route prefix.
        /// Trigger templates are relative to that prefix, while full URLs include "/{Prefix}".
        /// </summary>
        public const string Prefix = "api";
        public const string Root = $"/{Prefix}";

        public static class Templates
        {
            public const string About = "about";
            public const string Schemas = "schemas";
            public const string SchemaByName = "schemas/{schemaName}";
            public const string SchemaVersionDetail = "schemas/{schemaName}/versions/{version:int}";
            public const string CreateSubmission = "submissions/{schemeName}";
            public const string SubmissionStatus = "submissions/{submissionId:guid}";
            public const string UpdateSubmission = "submissions/{submissionId:guid}";
            public const string CheckSubmission = "submissions/{submissionId:guid}/check";
            public const string FinaliseSubmission = "submissions/{submissionId:guid}/finalise";
        }

        public const string About = $"{Root}/{Templates.About}";
        public const string Schemas = $"{Root}/{Templates.Schemas}";
        public const string CreateSubmissionTemplate = $"{Root}/submissions/{{schemaName}}";

        public static string SchemaByName(string schemaName)
        {
            return $"{Root}/{Templates.SchemaByName}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName)
            });
        }

        public static string SchemaVersionDetail(string schemaName, int version)
        {
            return $"{Root}/{Templates.SchemaVersionDetail}".FormatWith(new
            {
                schemaName = Uri.EscapeDataString(schemaName),
                version
            });
        }

        public static string CreateSubmission(string schemaName, int? version = null)
        {
            var basePath = $"{Root}/{Templates.CreateSubmission}".FormatWith(new
            {
                schemeName = Uri.EscapeDataString(schemaName)
            });
            return version.HasValue
                ? $"{basePath}?version={version.Value}"
                : basePath;
        }

        public static string SubmissionStatus(Guid submissionId)
        {
            return $"{Root}/{Templates.SubmissionStatus}".FormatWith(new { submissionId });
        }

        public static string UpdateSubmission(Guid submissionId)
        {
            return $"{Root}/{Templates.UpdateSubmission}".FormatWith(new { submissionId });
        }

        public static string CheckSubmission(Guid submissionId)
        {
            return $"{Root}/{Templates.CheckSubmission}".FormatWith(new { submissionId });
        }

        public static string FinaliseSubmission(Guid submissionId)
        {
            return $"{Root}/{Templates.FinaliseSubmission}".FormatWith(new { submissionId });
        }
    }
}
