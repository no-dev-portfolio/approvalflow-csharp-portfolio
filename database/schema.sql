CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Email] nvarchar(200) NOT NULL,
    [Name] nvarchar(80) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [PasswordHash] nvarchar(500) NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Requests] (
    [Id] int NOT NULL IDENTITY,
    [OwnerId] int NOT NULL,
    [Title] nvarchar(100) NOT NULL,
    [Purpose] nvarchar(2000) NOT NULL,
    [Amount] bigint NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Requests] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Amount] CHECK (Amount > 0 AND Amount <= 100000000),
    CONSTRAINT [CK_Status] CHECK (Status IN ('Draft','Submitted','Approved','Returned')),
    CONSTRAINT [FK_Requests_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Audits] (
    [Id] int NOT NULL IDENTITY,
    [RequestId] int NOT NULL,
    [ActorId] int NOT NULL,
    [Action] nvarchar(40) NOT NULL,
    [Comment] nvarchar(500) NOT NULL,
    [At] datetime2 NOT NULL,
    CONSTRAINT [PK_Audits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Audits_Requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [Requests] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Audits_Users_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO


CREATE INDEX [IX_Audits_ActorId] ON [Audits] ([ActorId]);
GO


CREATE INDEX [IX_Audits_RequestId_Id] ON [Audits] ([RequestId], [Id]);
GO


CREATE INDEX [IX_Requests_OwnerId_Status_Id] ON [Requests] ([OwnerId], [Status], [Id]);
GO


CREATE INDEX [IX_Requests_Status_Id] ON [Requests] ([Status], [Id]);
GO


CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
GO


