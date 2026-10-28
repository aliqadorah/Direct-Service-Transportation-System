using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Database.Migrations
{
    /// <summary>
    /// Revision 6 §B13–B14 (owner findings 27 Oct 2026): the view trn.TRN_TripPartnerCoverage with its index (DT-271 — which partner trips the
    /// partner's payments cover) and cfg.DashboardPins.WarnWhen (DT-275 — a pin can warn below its thresholds; existing pins stay "Above").
    /// </summary>
    public partial class Rev6_PartnerTripCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WarnWhen",
                schema: "cfg",
                table: "DashboardPins",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValueSql: "'Above'")
                .Annotation("Relational:DefaultConstraintName", "DF_DashboardPins_WarnWhen");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_PartnerOrder",
                schema: "trn",
                table: "TRN_Trips",
                columns: new[] { "TenantId", "CompanyId", "PartnerId", "TripDate", "ReceiptNoKey", "Id" },
                filter: "[PartnerId] IS NOT NULL AND [Status] = 'Active' AND [IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "PartnerCost" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DashboardPins_WarnWhen",
                schema: "cfg",
                table: "DashboardPins",
                sql: "[WarnWhen] IN ('Above','Below')");

            // DT-271: net payments of the partner (payments − repayments from the partner, active only — the DT-230 rule) allocated to its
            // active trips oldest first (trip date, receipt number, id); OpenAmount = the part of a trip's cost the payments do not cover.
            // A running total over every trip is too slow for 500,000 trips (row-mode window ≈ 0.7 s per 250,000), so the cost is summed per
            // day (a few hundred rows per partner, in the order of IX_TRN_Trips_PartnerOrder); the first day the payments do not cover is the
            // partner's boundary day (TRN_PartnerPaymentBoundary, one row per partner; none when everything is paid): earlier trips are paid,
            // later ones open, and only the boundary day's trips get an in-day running total (the APPLY runs only for them). A filter on
            // tenant, company or partner reaches the day totals before they are added up.
            migrationBuilder.Sql("""
                CREATE VIEW trn.TRN_PartnerPaymentBoundary AS
                WITH pay AS (
                    SELECT PartnerId, SUM(CASE WHEN Kind = 'Repayment' THEN -Amount ELSE Amount END) AS Paid
                    FROM trn.TRN_PartnerPayments WHERE Status = 'Active' AND IsDeleted = 0 GROUP BY PartnerId),
                day AS (
                    SELECT TenantId, CompanyId, PartnerId, TripDate, SUM(PartnerCost) AS DayCost
                    FROM trn.TRN_Trips WHERE PartnerId IS NOT NULL AND Status = 'Active' AND IsDeleted = 0
                    GROUP BY TenantId, CompanyId, PartnerId, TripDate),
                run AS (
                    SELECT d.TenantId, d.CompanyId, d.PartnerId, d.TripDate, ISNULL(p.Paid, 0) AS Paid,
                        SUM(d.DayCost) OVER (PARTITION BY d.TenantId, d.CompanyId, d.PartnerId ORDER BY d.TripDate ROWS UNBOUNDED PRECEDING) AS CostToDay
                    FROM day d LEFT JOIN pay p ON p.PartnerId = d.PartnerId)
                SELECT TenantId, CompanyId, PartnerId, CAST(MAX(Paid) AS decimal(18,3)) AS Paid,
                    MIN(CASE WHEN CostToDay > Paid THEN TripDate END) AS BoundaryDate,
                    CAST(ISNULL(MAX(CASE WHEN CostToDay <= Paid THEN CostToDay END), 0) AS decimal(18,3)) AS CostBefore
                FROM run GROUP BY TenantId, CompanyId, PartnerId;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW trn.TRN_TripPartnerCoverage AS
                SELECT t.Id AS TripId, t.TenantId, t.CompanyId, t.PartnerId, t.PartnerCost, b.Paid,
                    CAST(CASE WHEN b.BoundaryDate IS NULL OR t.TripDate < b.BoundaryDate THEN 0
                              WHEN t.TripDate > b.BoundaryDate THEN t.PartnerCost
                              WHEN b.CostBefore + s.InDay <= b.Paid THEN 0
                              WHEN b.CostBefore + s.InDay - t.PartnerCost >= b.Paid THEN t.PartnerCost
                              ELSE b.CostBefore + s.InDay - b.Paid END AS decimal(18,3)) AS OpenAmount
                FROM trn.TRN_Trips t
                JOIN trn.TRN_PartnerPaymentBoundary b ON b.TenantId = t.TenantId AND b.CompanyId = t.CompanyId AND b.PartnerId = t.PartnerId
                OUTER APPLY (SELECT SUM(x.PartnerCost) AS InDay FROM trn.TRN_Trips x
                             WHERE t.TripDate = b.BoundaryDate AND x.TenantId = t.TenantId AND x.CompanyId = t.CompanyId AND x.PartnerId = t.PartnerId
                               AND x.TripDate = t.TripDate AND x.Status = 'Active' AND x.IsDeleted = 0
                               AND (x.ReceiptNoKey < t.ReceiptNoKey OR (x.ReceiptNoKey = t.ReceiptNoKey AND x.Id <= t.Id))) s
                WHERE t.PartnerId IS NOT NULL AND t.Status = 'Active' AND t.IsDeleted = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS trn.TRN_TripPartnerCoverage;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS trn.TRN_PartnerPaymentBoundary;");

            migrationBuilder.DropIndex(
                name: "IX_TRN_Trips_PartnerOrder",
                schema: "trn",
                table: "TRN_Trips");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DashboardPins_WarnWhen",
                schema: "cfg",
                table: "DashboardPins");

            migrationBuilder.DropColumn(
                name: "WarnWhen",
                schema: "cfg",
                table: "DashboardPins")
                .Annotation("Relational:DefaultConstraintName", "DF_DashboardPins_WarnWhen");
        }
    }
}
