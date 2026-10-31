/* Central master (DDReview-CentralMaster): links a web login to a Google account so "Sign in with Google" can find it.
   A NEW table, so the old app is untouched. The link is by Google's stable account id (sub), never by email address.
   Safe to re-run. Run on a test copy first. */
IF OBJECT_ID(N'dbo.tblWebUserGoogle') IS NULL
BEGIN
    CREATE TABLE dbo.tblWebUserGoogle (
        GoogleSubject varchar(100) NOT NULL CONSTRAINT PK_tblWebUserGoogle PRIMARY KEY,
        WebUserID     int          NOT NULL,
        GoogleEmail   varchar(320) NULL,
        LinkedUtc     datetime2    NOT NULL CONSTRAINT DF_tblWebUserGoogle_Linked DEFAULT (sysutcdatetime()),
        CONSTRAINT FK_tblWebUserGoogle_User FOREIGN KEY (WebUserID) REFERENCES dbo.tblWebUserSecurity (WebUserID)
    );
    CREATE UNIQUE INDEX UX_tblWebUserGoogle_User ON dbo.tblWebUserGoogle (WebUserID);   -- one Google account per login
END
/* The web app's SQL login needs: GRANT SELECT, INSERT, DELETE ON dbo.tblWebUserGoogle TO [<central web login user>]; */
