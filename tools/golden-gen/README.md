# Golden-file generator (Stage 2, step 0 — DT-283)

Generated `tests/DirectService.Architecture.Tests/Golden/*.json` **once**, on 28 Oct 2026, from the restore tag
`pre-modules-final-2026-10-27` (commit `d0ef83d`). Kept only as a record of how the golden values were obtained.

- It compiles **only against the tag**: it reads the tag's own code paths (the namespace-based `EntityTypeName`, `UrlTokenService`
  keyed by `Type.Name`, the private `Types` maps of `EntityResolver` and `AuditDescriber`, the `nameof` email template names,
  the authentication set-up) and, where the tag had only an inline literal, the tag's source text.
- It is not part of the solution and is never built or run on the current code.

How it was run (worktree outside the repository, removed afterwards; the tag was never checked out in the main tree):

```bash
git worktree add --detach "$TEMP/dst-golden-tag" pre-modules-final-2026-10-27
cp -r tools/golden-gen "$TEMP/dst-golden-tag/tools/"
dotnet run --project "$TEMP/dst-golden-tag/tools/golden-gen" -- "$TEMP/dst-golden-tag" "$TEMP/dst-golden-out"
git worktree remove --force "$TEMP/dst-golden-tag"
```

Never regenerate the golden files from newer code: they are the reference the newer code is compared with.
