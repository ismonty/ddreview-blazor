/* Central master (DDReview-CentralMaster): one Google account can be linked to several logins (databases), a login can carry a friendly name,
   and top-level administrators can pick any login. All NEW tables except one key change on dbo.tblWebUserGoogle (see 1). Safe to re-run.
   Run on a test copy first. The old app is untouched.

   AFTER running: make yourself a top-level administrator (once). The web app ties the row to your Google account the first time you sign in with
   that email (Google must say the email is verified); after that the row is matched by Google's account id, never by email:

       insert dbo.tblDDReviewTopAdmin (GoogleEmail, AddedBy) values ('you@example.com', 'first setup');

   The web app's SQL login needs: GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tblWebUserGoogle, dbo.tblWebUserFriendlyName, dbo.tblDDReviewTopAdmin TO [<central web login user>];
                                  GRANT SELECT, INSERT ON dbo.tblDDReviewGoogleAudit TO [<central web login user>];                                          */

-- 1. A Google account may be linked to several logins: the key becomes (GoogleSubject, WebUserID). A login still has at most one Google account
--    (UX_tblWebUserGoogle_User stays), so the old rule "one person per login" holds.
IF OBJECT_ID(N'dbo.tblWebUserGoogle') IS NOT NULL
   AND (SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        WHERE i.object_id = OBJECT_ID(N'dbo.tblWebUserGoogle') AND i.is_primary_key = 1) = 1
BEGIN
    ALTER TABLE dbo.tblWebUserGoogle DROP CONSTRAINT PK_tblWebUserGoogle;
    ALTER TABLE dbo.tblWebUserGoogle ADD CONSTRAINT PK_tblWebUserGoogle PRIMARY KEY (GoogleSubject, WebUserID);
END

-- 2. The name people see in the "choose a database" list.
IF OBJECT_ID(N'dbo.tblWebUserFriendlyName') IS NULL
    CREATE TABLE dbo.tblWebUserFriendlyName (
        WebUserID    int          NOT NULL CONSTRAINT PK_tblWebUserFriendlyName PRIMARY KEY,
        FriendlyName varchar(100) NOT NULL,
        UpdatedUtc   datetime2    NOT NULL CONSTRAINT DF_tblWebUserFriendlyName_Updated DEFAULT (sysutcdatetime()),
        UpdatedBy    varchar(320) NULL,
        CONSTRAINT FK_tblWebUserFriendlyName_User FOREIGN KEY (WebUserID) REFERENCES dbo.tblWebUserSecurity (WebUserID)
    );

-- 3. Top-level administrators: may pick any login and edit friendly names. GoogleSubject is filled in at their first Google sign-in.
IF OBJECT_ID(N'dbo.tblDDReviewTopAdmin') IS NULL
BEGIN
    CREATE TABLE dbo.tblDDReviewTopAdmin (
        TopAdminID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblDDReviewTopAdmin PRIMARY KEY,
        GoogleEmail  varchar(320) NOT NULL,
        GoogleSubject varchar(100) NULL,
        AddedUtc     datetime2    NOT NULL CONSTRAINT DF_tblDDReviewTopAdmin_Added DEFAULT (sysutcdatetime()),
        AddedBy      varchar(320) NULL
    );
    CREATE UNIQUE INDEX UX_tblDDReviewTopAdmin_Email ON dbo.tblDDReviewTopAdmin (GoogleEmail);
    CREATE UNIQUE INDEX UX_tblDDReviewTopAdmin_Subject ON dbo.tblDDReviewTopAdmin (GoogleSubject) WHERE GoogleSubject IS NOT NULL;
END

-- 4. Who linked, unlinked, picked or renamed what (insert only).
IF OBJECT_ID(N'dbo.tblDDReviewGoogleAudit') IS NULL
BEGIN
    CREATE TABLE dbo.tblDDReviewGoogleAudit (
        AuditID       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblDDReviewGoogleAudit PRIMARY KEY,
        AtUtc         datetime2    NOT NULL CONSTRAINT DF_tblDDReviewGoogleAudit_At DEFAULT (sysutcdatetime()),
        GoogleSubject varchar(100) NULL,
        GoogleEmail   varchar(320) NULL,
        Action        varchar(30)  NOT NULL,
        WebUserID     int          NULL,
        Detail        varchar(500) NULL,
        ClientIp      varchar(50)  NULL
    );
    CREATE INDEX IX_tblDDReviewGoogleAudit_At ON dbo.tblDDReviewGoogleAudit (AtUtc);
END
