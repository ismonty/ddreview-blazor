/* Central master (DDReview-CentralMaster): marks logins (databases) as inactive. An inactive login is left out of the "Choose a database" list
   for everyone except top-level administrators (who can show inactive ones and switch them back). It does NOT block the password login page,
   so the old and new sites behave the same. A login with NO row here is active. A NEW table; the old app is untouched. Safe to re-run.
   The web app's SQL login needs: GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tblWebUserInactive TO [<central web login user>]; */
IF OBJECT_ID(N'dbo.tblWebUserInactive') IS NULL
    CREATE TABLE dbo.tblWebUserInactive (
        WebUserID int          NOT NULL CONSTRAINT PK_tblWebUserInactive PRIMARY KEY,
        SetUtc    datetime2    NOT NULL CONSTRAINT DF_tblWebUserInactive_Set DEFAULT (sysutcdatetime()),
        SetBy     varchar(320) NULL,
        CONSTRAINT FK_tblWebUserInactive_User FOREIGN KEY (WebUserID) REFERENCES dbo.tblWebUserSecurity (WebUserID)
    );
