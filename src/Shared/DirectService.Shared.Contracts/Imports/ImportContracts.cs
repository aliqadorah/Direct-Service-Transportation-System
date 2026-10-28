namespace DirectService.Shared.Contracts;

// Imports (FR-109, FR-114; ADR-13), shared by every importer (Stage 2 session 7, DT-316).

/// <summary>Preview of an import: every row with its problems; nothing is saved while any row has a problem (strict mode).</summary>
public sealed record ImportPreview(int TotalRows, int ValidRows, int RejectedRows, IReadOnlyList<ImportRowResult> Rows);

/// <summary>
/// One row of an import (1-based row number of the sheet, header = row 1) with its problems in the user's language; <paramref name="Details"/>
/// gives each problem's column and how to fix it (owner feedback 13 Oct 2026).
/// </summary>
public sealed record ImportRowResult(int Row, string? Key, IReadOnlyList<string> Problems, IReadOnlyList<ImportProblem>? Details = null);

/// <summary>One problem of an import row: the column of the file (null when it concerns the whole row), the problem and how to fix it.</summary>
public sealed record ImportProblem(string Code, string? Column, string Message, string Fix);

public sealed record ImportResult(string BatchId, int ImportedRows, int TotalRows);

/// <summary>A generated file (export, voucher data) returned to the client.</summary>
public sealed record GeneratedFile(string FileName, string ContentType, byte[] Content);
