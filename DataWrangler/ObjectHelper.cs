﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataWrangler.DBOs;
using LiteDB;
using PasswordGenerator;

namespace DataWrangler
{
    public class ObjectHelper : IDisposable
    {
        public const int DefaultRecordSetSize = 500;
        private readonly DataAccess _dA;

        public ObjectHelper(string connectionString, UserAccount user = null)
        {
            _dA = new DataAccess(connectionString, user, user == null);
        }

        public ObjectHelper(Dictionary<string, string> dbSettings, UserAccount user = null)
        {
            _dA = new DataAccess(ConfigurationHelper.GetConnectionString(dbSettings), user, user == null);
        }

        public void Dispose()
        {
            _dA.Dispose();
        }

        public static StatusObject InitializeSystem(string dbPath, bool dbEncrypt = false, bool overwrite = false)
        {
            if (!overwrite && new FileInfo(dbPath).Exists)
                return new StatusObject
                {
                    OperationType = StatusObject.OperationTypes.Delete,
                    Result = "Database file already exists!",
                    Success = false
                };
            try
            {
                File.Delete(dbPath);
            }
            catch (Exception e)
            {
                return new StatusObject
                {
                    OperationType = StatusObject.OperationTypes.Delete,
                    Result = e,
                    Success = false
                };
            }

            var pwGenerator = new Password(12).IncludeLowercase().IncludeUppercase().IncludeNumeric()
                .IncludeSpecial("!@#$%^&*()-_=+");

            var newUser = "sysadmin";
            var newPass = "P@ssw0rd";

            string dbPass = null;

            if (dbEncrypt) dbPass = pwGenerator.Next();

            ConfigurationHelper.SaveDbSettings(dbPath, dbEncrypt, dbPass);

            StatusObject status = null;

            var dbSettings = ConfigurationHelper.GetDbSettings();

            using (var self = new ObjectHelper(dbSettings))
            {
                status = self.AddUserAccount(newUser, newPass, true);
            }

            if (status.Success)
                status = new StatusObject
                {
                    OperationType = StatusObject.OperationTypes.System,
                    Result = dbSettings,
                    Success = true
                };

            return status;
        }

        public StatusObject RebuildDb(Dictionary<string, string> dbSettings, bool usePassword = false,
            string newPassword = null)
        {
            return _dA.RebuildDatabase(dbSettings, usePassword, newPassword);
        }

        #region RecordType Accessors

        public StatusObject AddRecordType(string name, List<string> attributes, bool active)
        {
            var recordAttributes = new Dictionary<string, string>();

            foreach (var attr in attributes) recordAttributes.Add(DataProcessor.GetStrId(), attr);

            var newRecordType = new RecordType
            {
                Name = name,
                Attributes = recordAttributes,
                Active = active
            };
            return _dA.InsertObject(newRecordType, null, "Name", true);
        }

        public StatusObject AddRecordTypes(string[] names, List<string>[] attributes, bool[] actives)
        {
            if (names.Length == attributes.Length && names.Length == actives.Length)
            {
                var newRecordTypes = new RecordType[names.Length];
                for (var i = 0; i < names.Length; i++)
                {
                    var recordAttributes = new Dictionary<string, string>();

                    foreach (var attr in attributes[i]) recordAttributes.Add(DataProcessor.GetStrId(), attr);

                    newRecordTypes[i] = new RecordType
                    {
                        Name = names[i],
                        Attributes = recordAttributes,
                        Active = actives[i]
                    };
                }


                return _dA.InsertObjects(newRecordTypes);
            }

            return _dA.GetStatusObject(StatusObject.OperationTypes.Create, "Mismatched number of values provided!",
                false);
        }

        public StatusObject GetRecordTypeById(int id)
        {
            return _dA.GetObjectById<RecordType>(id);
        }

        public StatusObject GetRecordTypeCount()
        {
            return _dA.GetCountOfObj<RecordType>();
        }

        public StatusObject GetRecordTypes(int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetObjectsByType<RecordType>(skip, limit);
        }

        public StatusObject UpdateRecordType(RecordType rT)
        {
            rT.LastUpdated = DateTime.UtcNow;
            return _dA.UpdateObject(rT);
        }

        public StatusObject DeleteRecordType(RecordType rT, bool deleteOrphanedRecords)
        {
            if (deleteOrphanedRecords)
            {
                var deleteRecordTypeStatus = _dA.DeleteObject<RecordType>(rT);
                if (deleteRecordTypeStatus.Success)
                {
                    var deleteCollectionStatus = _dA.DeleteCollection<Record>("Record_" + rT.Id);
                    if (deleteCollectionStatus.Success)
                    {
                        return _dA.DeleteFileOfRecordType(rT);
                    }

                    return deleteCollectionStatus;
                }

                return deleteRecordTypeStatus;
            }

            return _dA.DeleteObject<RecordType>(rT);

        }

        #endregion

        #region Record Accessors

        public StatusObject AddRecord(RecordType rT, Dictionary<string, string> attributes, bool active)
        {
            if (!attributes.Keys.Except(rT.Attributes.Keys).Any())
            {
                var newRecord = new Record
                {
                    TypeId = rT.Id,
                    Attributes = attributes,
                    Active = active
                };
                return _dA.InsertObject(newRecord, "Record_" + rT.Id);
            }

            return _dA.GetStatusObject(StatusObject.OperationTypes.Create,
                "Record contains attributes unknown to the RecordType definition", false);
        }

        public StatusObject AddRecords(Record[] records, RecordType rT)
        {
            return _dA.InsertObjects(records, "Record_" + rT.Id);
        }

        public StatusObject AddAttachmentsToRecord(Record r, string[] attachmentPaths)
        {
            foreach (var file in attachmentPaths)
                if (!File.Exists(file))
                    return _dA.GetStatusObject(StatusObject.OperationTypes.Create,
                        "File specified for attachment is inaccessible", false);

            var uploadResults = _dA.AddFilesToRecord(r, attachmentPaths);
            if (uploadResults.Success)
            {
                r.Attachments = (List<string>) uploadResults.Result;
                return UpdateRecord(r);
            }

            return uploadResults;
        }

        public StatusObject GetRecordById(int id, RecordType rT)
        {
            return _dA.GetObjectById<Record>(id, "Record_" + rT.Id);
        }

        public StatusObject GetRecordsByType(RecordType rT, int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetObjectsByType<Record>(skip, limit, "Record_" + rT.Id);
        }

        public StatusObject GetRecordCountByRecordType(RecordType rT)
        {
            return _dA.GetCountOfObj<Record>("Record_" + rT.Id);
        }

        public StatusObject GetRecordCountByRecordTypeAndSearch(RecordType rT, string searchField, string searchValue)
        {
            var expr = BsonExpression.Create(string.Format("{0} like \"%{1}%\"", searchField, searchValue));
            return _dA.GetCountOfObjByExpr<Record>(expr, "Record_" + rT.Id);
        }

        public StatusObject GetRecordCountByRecordTypeAndGlobalSearch(RecordType rT, string searchValue)
        {
            return _dA.GetCountOfRecordsByGlobalSearch(rT, searchValue, "Record_" + rT.Id);
        }

        public StatusObject UpdateRecord(Record r)
        {
            r.LastUpdated = DateTime.UtcNow;
            return _dA.UpdateObject(r, "Record_" + r.TypeId);
        }

        public StatusObject DeleteRecord(Record r)
        {
            if (r.Attachments.Count > 0)
                foreach (var attachment in r.Attachments)
                {
                    var delAttachmentResult = _dA.DeleteFileFromRecord(r, attachment);
                    if (!delAttachmentResult.Success) return delAttachmentResult;
                }

            return _dA.DeleteObject<Record>(r);
        }

        public StatusObject DeleteAttachmentFromRecord(Record r, string fileId)
        {
            return _dA.DeleteFileFromRecord(r, fileId);
        }

        public StatusObject SaveFileFromRecord(string fileId, string outputPath)
        {
            return _dA.SaveFile(fileId, outputPath);
        }

        #endregion

        #region UserAccount Accessors

        public StatusObject AddUserAccount(string username, string password, bool active)
        {
            var newUserAccount = new UserAccount
            {
                Username = username,
                Password = UserAccount.GetPasswordHash(password),
                Active = active
            };
            return _dA.InsertObject(newUserAccount, null, "Username", true);
        }

        public StatusObject GetUserAccountById(int id)
        {
            return _dA.GetObjectById<UserAccount>(id);
        }

        public StatusObject GetUserAccounts(int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetObjectsByType<UserAccount>(skip, limit);
        }

        public StatusObject GetUserAccountCount()
        {
            return _dA.GetCountOfObj<UserAccount>();
        }

        public StatusObject UpdateUserAccount(UserAccount uA)
        {
            uA.LastUpdated = DateTime.UtcNow;
            return _dA.UpdateObject(uA);
        }

        public StatusObject LoginUserAccount(string username, string password)
        {
            var result = GetUserAccountByUsername(username);
            if (result.Success && result.Result != null)
            {
                var storedHash = ((UserAccount) result.Result).Password;
                var hashBytes = Convert.FromBase64String(storedHash);

                var salt = new byte[16];
                Array.Copy(hashBytes, 0, salt, 0, 16);

                var calculatedHash = UserAccount.GetPasswordHash(password, salt);
                if (calculatedHash.Equals(storedHash))
                    result.Result = (UserAccount) result.Result;
            }

            return result;
        }

        #endregion

        #region Searches

        public StatusObject GetRecordsByTypeSearch(RecordType rT, string searchField, string searchValue, int skip = 0,
            int limit = DefaultRecordSetSize)
        {
            var expr = BsonExpression.Create(string.Format("{0} like \"%{1}%\"", searchField,
                DataProcessor.SafeString(searchValue)));
            return _dA.GetRecordsByExprSearch(expr, skip, limit, "Record_" + rT.Id);
        }

        public StatusObject GetRecordsByGlobalSearch(RecordType rT, string searchValue, int skip = 0,
            int limit = DefaultRecordSetSize)
        {
            return _dA.GetRecordsByGlobalSearch(rT, searchValue, skip, limit, "Record_" + rT.Id);
        }

        public StatusObject GetUserAccountByUsername(string username)
        {
            return _dA.GetObjectByFieldSearch<UserAccount>("username", username);
        }

        #endregion

        #region AuditEntries

        public StatusObject GetAuditEntriesByUsername(string username, int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetAuditEntriesByUsername(username, skip, limit);
        }

        public StatusObject GetRecordAuditEntries(int objectId, int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetAuditEntriesByField<Record>("ObjectId", objectId, skip, limit);
        }

        public StatusObject GetRecordTypeAuditEntries(int objectId, int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetAuditEntriesByField<RecordType>("ObjectId", objectId, skip, limit);
        }

        public StatusObject GetUserAccountAuditEntries(int objectId, int skip = 0, int limit = DefaultRecordSetSize)
        {
            return _dA.GetAuditEntriesByField<Record>("ObjectId", objectId, skip, limit);
        }

        public StatusObject GetAuditEntryCount()
        {
            return _dA.GetCountOfObj<AuditEntry>();
        }

        #endregion
    }
}