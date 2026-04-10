#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.ProjectAuditor.Editor;
using UnityEngine;

public static class ProjectAuditorCI {
  public static void AuditAndExport() {
    var outputDirectory = Path.GetFullPath("artifacts/project-auditor");
    Directory.CreateDirectory(outputDirectory);

    var reportPath = Path.Combine(outputDirectory, "project-auditor.projectauditor");
    var summaryPath = Path.Combine(outputDirectory, "summary.txt");

    var projectAuditor = new ProjectAuditor();
    Report report = projectAuditor.Audit();
    report.Save(reportPath);

    IReadOnlyCollection<ReportItem> codeIssues = report.FindByCategory(IssueCategory.Code);
    IReadOnlyCollection<ReportItem> projectIssues = report.FindByCategory(IssueCategory.ProjectSetting);
    IReadOnlyCollection<ReportItem> assetIssues = report.FindByCategory(IssueCategory.AssetIssue);
    var summary =
      $"Project Auditor report saved to: {reportPath}\n" +
      $"Code issues: {codeIssues.Count}\n" +
      $"Project setting issues: {projectIssues.Count}\n" +
      $"Asset issues: {assetIssues.Count}\n";

    File.WriteAllText(summaryPath, summary);
    Debug.Log(summary);
  }
}
#endif
