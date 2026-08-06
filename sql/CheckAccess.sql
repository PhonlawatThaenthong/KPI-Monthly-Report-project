SELECT TOP 10 OccurredAt, UserName, ActionType, Detail, IsSuccess
FROM meta.AuditLog
WHERE ActionType = 'ACCESS_DENIED'
ORDER BY AuditId DESC;