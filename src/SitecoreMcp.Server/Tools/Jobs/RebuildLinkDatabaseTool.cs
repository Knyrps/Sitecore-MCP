using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Jobs;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Jobs
{
    /// <summary>Arguments for <see cref="RebuildLinkDatabaseTool"/>.</summary>
    public sealed class RebuildLinkDatabaseArgs
    {
        /// <summary>The database whose links to rebuild; defaults to master.</summary>
        [McpParam(Description = "Database whose link records to rebuild. Defaults to 'master'.")]
        public string Database { get; set; }
    }

    /// <summary>
    /// Rebuilds the Link Database for one database as a background job. The Link Database backs the
    /// reference tools, so a rebuild is the fix when their results look stale or incomplete.
    /// </summary>
    public sealed class RebuildLinkDatabaseTool : McpTool<RebuildLinkDatabaseArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_rebuild_link_database";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Rebuild the Link Database for a database (default master) - the store behind " +
            "sitecore_get_item_references/referrers. Run it when reference results look stale " +
            "(after a bulk import or serialization sync). Walks every item, so it runs as a " +
            "background job: poll sitecore_get_jobs with the returned handle. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RebuildLinkDatabaseArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(args.Database);

            var options = new DefaultJobOptions(
                $"MCP_RebuildLinkDatabase_{db.Name}",
                "linkDatabase",
                Sitecore.Context.Site?.Name ?? "shell",
                new Runner(db),
                nameof(Runner.Run));

            var job = JobManager.Start(options);

            var result = new JObject
            {
                ["database"] = db.Name,
                ["job"] = JobDescriber.Describe(job),
                ["note"] = "The rebuild walks every item in the database and runs in the background. " +
                           "Poll sitecore_get_jobs with the job handle."
            };
            return McpToolResult.Structured(result);
        }

        /// <summary>Executes the rebuild on the job thread.</summary>
        public sealed class Runner
        {
            private readonly Database _database;

            /// <summary>Creates a runner bound to the database to rebuild.</summary>
            public Runner(Database database)
            {
                _database = database;
            }

            /// <summary>Invoked by the job engine.</summary>
            public void Run()
            {
                Sitecore.Globals.LinkDatabase.Rebuild(_database);
            }
        }
    }
}
