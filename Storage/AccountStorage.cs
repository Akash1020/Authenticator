﻿using Domain;
using Domain.Exceptions;
using Newtonsoft.Json;
using Synchronization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Domain.Storage
{
    public class AccountStorage
    {
        private StorageFolder applicationData;
        private List<Account> accounts;
        private Account removedAccount;
        private ISynchronizer synchronizer;
        private int removedIndex;
        private string plainStorage;
        private static AccountStorage instance;
        private static object syncRoot = new object();

        private const string ACCOUNTS_FILENAME = "Accounts.json";
        private const string TEMP_ACCOUNTS_FILENAME = "Accounts-temp.json";
        private const string DESCRIPTOR = "LOCAL=user";

        public event EventHandler SynchronizationStarted;
        public event EventHandler<SynchronizationResult> SynchronizationCompleted;

        public static AccountStorage Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                        {
                            instance = new AccountStorage();
                        }
                    }
                }

                return instance;
            }
        }

        public bool IsSynchronizing { get; private set; }

        private AccountStorage()
        {
            applicationData = ApplicationData.Current.LocalFolder;
        }

        private void NotifySynchronizationStarted()
        {
            IsSynchronizing = true;

            if (SynchronizationStarted != null)
            {
                SynchronizationStarted(this, null);
            }
        }

        private void NotifySynchronizationCompleted(SynchronizationResult synchronizationResult)
        {
            IsSynchronizing = false;

            if (SynchronizationCompleted != null)
            {
                SynchronizationCompleted(this, synchronizationResult);
            }
        }

        public async Task<IReadOnlyList<Account>> GetAllAsync()
        {
            if (accounts == null)
            {
                await LoadStorage();
            }

            return accounts;
        }

        private async Task LoadStorage()
        {
            StorageFile file = null;
            accounts = new List<Account>();

            try
            {
                file = await applicationData.CreateFileAsync(ACCOUNTS_FILENAME, CreationCollisionOption.OpenIfExists);
            }
            catch
            {
                // File does not exist yet. We're going to create it shortly
            }

            string content = null;

            try
            {
                DataProtectionProvider provider = new DataProtectionProvider(DESCRIPTOR);
                IBuffer buffer = await FileIO.ReadBufferAsync(file);

                // Only decrypt the file if the buffer contains content
                if (buffer.Length > 0)
                {
                    IBuffer bufferContent = await provider.UnprotectAsync(buffer);
                    content = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, bufferContent);
                }
            }
            catch
            {
                // File was just created or corrupted.
            }

            if (string.IsNullOrWhiteSpace(content) || content == "null")
            {
                accounts = new List<Account>();
            }
            else
            {
                accounts = JsonConvert.DeserializeObject<List<Account>>(content);
            }

            Clean();
            UpdatePlainStorage();
        }

        private void UpdatePlainStorage()
        {
            plainStorage = JsonConvert.SerializeObject(accounts);
        }

        private async void Clean()
        {
            List<Account> invalidAccounts = new List<Account>();

            foreach (Account account in accounts)
            {
                try
                {
                    OTP otp = new OTP(account.Secret);
                }
                catch
                {
                    invalidAccounts.Add(account);
                }
            }

            if (invalidAccounts.Count > 0)
            {
                foreach (Account invalidAccount in invalidAccounts)
                {
                    await RemoveAsync(invalidAccount);
                }
            }
        }

        public void SetSynchronizer(ISynchronizer synchronizer)
        {
            this.synchronizer = synchronizer;
        }

        public async Task UpdateLocalFromRemote()
        {
            NotifySynchronizationStarted();

            SynchronizationResult result = null;

            if (synchronizer != null)
            {
                result = await synchronizer.UpdateLocalFromRemote(accounts);

                if (result.Accounts != null)
                {
                    accounts = result.Accounts.ToList();

                    await Persist(false);
                }
            }

            NotifySynchronizationCompleted(result);
        }

        public async Task Synchronize()
        {
            if (synchronizer != null)
            {
                SynchronizationResult result = await synchronizer.Synchronize(accounts);

                if (result.Accounts != null)
                {
                    accounts = result.Accounts.ToList();
                }

                await Persist(false);
            }
        }

        private async Task Persist(bool persistRemote = true)
        {
            if (accounts == null)
            {
                accounts = new List<Account>();
                UpdatePlainStorage();
            }

            try
            {
                if (persistRemote)
                {
                    await UpdateRemoteFromLocal();
                }

                StorageFile tempFile = await applicationData.CreateFileAsync(TEMP_ACCOUNTS_FILENAME, CreationCollisionOption.ReplaceExisting);
                StorageFile currentFile = await applicationData.CreateFileAsync(ACCOUNTS_FILENAME, CreationCollisionOption.OpenIfExists);

                try
                {
                    DataProtectionProvider provider = new DataProtectionProvider(DESCRIPTOR);

                    string data = JsonConvert.SerializeObject(accounts);

                    IBuffer buffMsg = CryptographicBuffer.ConvertStringToBinary(data, BinaryStringEncoding.Utf8);
                    IBuffer buffProtected = await provider.ProtectAsync(buffMsg);

                    await FileIO.WriteBufferAsync(tempFile, buffProtected);

                    await tempFile.MoveAndReplaceAsync(currentFile);

                    UpdatePlainStorage();
                }
                catch
                {
                    // TODO: Add logging.
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public async Task SaveAsync(Account account)
        {
            if (accounts == null)
            {
                await LoadStorage();
            }

            if (!account.IsModified)
            {
                if (accounts.Any(a => a.Equals(account)))
                {
                    throw new DuplicateAccountException();
                }

                // This is a new account
                accounts.Add(account);
            }

            account.Flush();

            await Persist();
        }

        private async Task UpdateRemoteFromLocal()
        {
            if (synchronizer != null)
            {
                NotifySynchronizationStarted();

                SynchronizationResult result = await synchronizer.UpdateRemoteFromLocal(plainStorage, accounts);

                NotifySynchronizationCompleted(result);
            }
        }

        public async Task RemoveAsync(Account account)
        {
            if (accounts == null)
            {
                await LoadStorage();
            }
            
            removedAccount = account;
            removedIndex = accounts.IndexOf(account);
            accounts.Remove(account);

            await Persist();
        }

        public async Task ReorderAsync(int fromIndex, int toIndex)
        {
            Account account = accounts.ElementAt(fromIndex);

            accounts.Remove(account);
            accounts.Insert(toIndex, account);

            await Persist();
        }

        public async Task UndoRemoveAsync()
        {
            accounts.Insert(removedIndex, removedAccount);

            await Persist();
        }

        public async Task<string> GetPlainStorageAsync()
        {
            if (accounts == null)
            {
                await LoadStorage();
            }

            return JsonConvert.SerializeObject(accounts);
        }
    }
}
