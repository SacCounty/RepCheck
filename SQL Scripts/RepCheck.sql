/****** REPCHECK REPORT ******/
SELECT report.objectstorename, report.ReportDate, 
	Coalesce(missing.Count, 0) as 'Missing', 
	Coalesce(orphaned.Count, 0) as 'Orphaned'
  FROM RepCheckReport report
  LEFT OUTER JOIN (
	SELECT m.reportid, Count(*) as Count from missingfile m GROUP BY reportid) missing
  ON report.ReportID = missing.reportid
  left outer join (
	SELECT o.reportid, Count(*) as Count from OrphanedFile o GROUP BY ReportID) orphaned
  ON report.reportid = orphaned.reportid
  ORDER BY report.ReportDate DESC