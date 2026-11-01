INSERT INTO System_Notification (Id, Type, Title, Body, ExpiresAt, IsDismissed, DismissedAt, DismissTriggerKey, DismissReason, DateCreated, IsDeleted) VALUES
  ('a0000278-0000-4000-8000-000000000001','ActionRequired','Smoke test action required','A #278 smoke test row needing an action.',NULL,0,NULL,'DatabaseReset',NULL,'2026-01-01 00:00:00',0),
  ('a0000278-0000-4000-8000-000000000002','Information','Smoke test expired','A #278 smoke test row that has already expired.','2020-01-01 00:00:00',0,NULL,NULL,NULL,'2026-01-01 00:00:00',0),
  ('a0000278-0000-4000-8000-000000000003','Information','Smoke test dismissed','A #278 smoke test row already dismissed.',NULL,1,'2026-01-02 00:00:00',NULL,NULL,'2026-01-01 00:00:00',0),
  ('a0000278-0000-4000-8000-000000000004','Information','Smoke test obsolete','A #278 smoke test row whose subject no longer exists.',NULL,1,'2026-01-02 00:00:00',NULL,'Obsolete','2026-01-01 00:00:00',0);
