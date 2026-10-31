/* Central master: password hashes live in a NEW table, so the old app keeps working during the transition.
   Run on DDReview-CentralMaster (test copy first). Then set Tenancy:UseCredentialTable=true. */
IF OBJECT_ID(N'dbo.tblWebUserCredential') IS NULL
BEGIN
    CREATE TABLE dbo.tblWebUserCredential (
        WebUserID    int          NOT NULL CONSTRAINT PK_tblWebUserCredential PRIMARY KEY,
        PasswordHash varchar(200) NOT NULL,
        UpdatedUtc   datetime2    NOT NULL,
        CONSTRAINT FK_tblWebUserCredential_User FOREIGN KEY (WebUserID) REFERENCES dbo.tblWebUserSecurity (WebUserID)
    );
END
