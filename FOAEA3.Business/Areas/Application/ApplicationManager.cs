﻿using DBHelper;
using FOAEA3.Common.Helpers;
using FOAEA3.Common.Models;
using FOAEA3.Data.Base;
using FOAEA3.Model;
using FOAEA3.Model.Constants;
using FOAEA3.Model.Enums;
using FOAEA3.Model.Interfaces;
using FOAEA3.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FOAEA3.Business.Areas.Application
{
    internal partial class ApplicationManager
    {
        private readonly ApplicationData Application;

        public IRepositories DB { get; }
        public ApplicationEventManager EventManager { get; }
        public ApplicationEventDetailManager EventDetailManager { get; }

        protected ApplicationValidation Validation { get; }
        protected readonly CustomConfig config;

        protected ApplicationStateEngine StateEngine { get; }

        protected string Appl_EnfSrv_Cd => Application.Appl_EnfSrv_Cd?.Trim();

        protected string Appl_CtrlCd => Application.Appl_CtrlCd?.Trim();

        private FoaeaUser _currentUser;

        public FoaeaUser CurrentUser
        {
            get
            {
                return _currentUser;
            }
            set
            {
                _currentUser = value;

                if (Validation is not null)
                    Validation.CurrentUser = this.CurrentUser;

                if (DB is not null)
                    DB.CurrentSubmitter = this.CurrentUser.Submitter.Subm_SubmCd;
            }
        }

        public async Task SetCurrentUserAsync(ClaimsPrincipal user)
        {
            CurrentUser = await UserHelper.ExtractDataFromUser(user, DB);
        }

        public ApplicationManager(ApplicationData applicationData, IRepositories repositories, CustomConfig config,
                                  ApplicationValidation applicationValidation = null)
        {
            this.config = config;
            Application = applicationData;
            EventManager = new ApplicationEventManager(Application, repositories);
            EventDetailManager = new ApplicationEventDetailManager(Application, repositories);
            DB = repositories;
            if (applicationValidation is null)
                Validation = new ApplicationValidation(Application, EventManager, DB, config, CurrentUser);
            else
            {
                Validation = applicationValidation;
                Validation.EventManager = EventManager;
            }

            StateEngine = new ApplicationStateEngine(
                process_00_InitialState: Process_00_InitialState,
                process_01_InvalidApplication: Process_01_InvalidApplication,
                process_02_AwaitingValidation: Process_02_AwaitingValidation,
                process_03_SinConfirmationPending: Process_03_SinConfirmationPending,
                process_04_SinConfirmed: Process_04_SinConfirmed,
                process_05_SinNotConfirmed: Process_05_SinNotConfirmed,
                process_06_PendingAcceptanceSwearing: Process_06_PendingAcceptanceSwearing,
                process_07_ValidAffidavitNotReceived: Process_07_ValidAffidavitNotReceived,
                process_09_ApplicationRejected: Process_09_ApplicationRejected,
                process_10_ApplicationAccepted: Process_10_ApplicationAccepted,
                process_11_ApplicationReinstated: Process_11_ApplicationReinstated,
                process_12_PartiallyServiced: Process_12_PartiallyServiced,
                process_13_FullyServiced: Process_13_FullyServiced,
                process_14_ManuallyTerminated: Process_14_ManuallyTerminated,
                process_15_Expired: Process_15_Expired,
                process_17_FinancialTermsVaried: Process_17_FinancialTermsVaried,
                process_19_AwaitingDocumentsForVariation: Process_19_AwaitingDocumentsForVariation,
                process_35_ApplicationSuspended: Process_35_ApplicationSuspended,
                process_91_InvalidVariationSource: Process_91_InvalidVariationSource,
                process_92_InvalidVariationFinTerms: Process_92_InvalidVariationFinTerms,
                process_93_ValidFinancialVariation: Process_93_ValidFinancialVariation,
                invalidStateChange: InvalidStateChange
            );
        }

        public async Task SetNewStateTo(ApplicationState newState)
        {
            await StateEngine.SetNewStateTo(Application.AppLiSt_Cd, newState);
        }

        public ApplicationState GetState()
        {
            return Application.AppLiSt_Cd;
        }

        public string GetCategory()
        {
            return Application.AppCtgy_Cd;
        }

        protected bool IsValidCategory(string category)
        {
            if (Application.AppCtgy_Cd != category)
            {
                Application.Messages.AddError($"Invalid category type ({Application.AppCtgy_Cd}). Expected {category}.");
                return false;
            }
            else
                return true;
        }

        public virtual async Task<bool> LoadApplicationAsync(string enfService, string controlCode)
        {
            var data = await DB.ApplicationTable.GetApplicationAsync(enfService, controlCode);

            if ((data != null) && (CurrentUser is not null))
            {

                if (CurrentUserHasReadAccess(enfService, data.Subm_SubmCd))
                {
                    Application.Merge(data);
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> ApplicationExistsAsync()
        {
            if (!String.IsNullOrEmpty(Appl_CtrlCd))
            {
                return await DB.ApplicationTable.ApplicationExistsAsync(Appl_EnfSrv_Cd, Appl_CtrlCd);
            }
            else
                return false;
        }

        public virtual async Task<bool> CreateApplicationAsync()
        {
            if ((CurrentUser is null) ||
                (!CurrentUserHasFullAccess(Application.Appl_EnfSrv_Cd, Application.Subm_SubmCd)))
            {
                Application.Messages.AddError(ErrorResource.CANT_CREATE_OR_MODIFY_APPLICATION_UNAUTHORIZED);
                return false;
            }

            if (await ApplicationExistsAsync())
            {
                Application.Messages.AddError(ErrorResource.CANT_CREATE_APPLICATION_ALREADY_EXISTS);
                return false;
            }

            // clean data
            TrimSpaces();
            MakeUpperCase();

            Application.Appl_Create_Dte = DateTime.Now;
            Application.Appl_Create_Usr = DB.CurrentSubmitter;
            Application.Appl_LastUpdate_Dte = DateTime.Now;
            Application.Appl_LastUpdate_Usr = DB.CurrentSubmitter;

            Application.Appl_Lgl_Dte = Application.Appl_Lgl_Dte.Date; // remove time

            Application.Appl_Rcptfrm_Dte = DateTime.Now.Date;

            // generate control code if not entered
            if (String.IsNullOrEmpty(Appl_CtrlCd))
            {
                Application.Appl_CtrlCd = await DB.ApplicationTable.GenerateApplicationControlCodeAsync(Appl_EnfSrv_Cd);
                Validation.IsSystemGeneratedControlCode = true;
            }

            // add reminder
            if (Application.AppCtgy_Cd.NotIn("L03"))
                EventManager.AddBFEvent(EventCode.C50528_BF_10_DAYS_FROM_RECEIPT_OF_APPLICATION);

            // validate data and decide on what state to bring the application to
            await Process_00_InitialState();

            if (Application.Messages.ContainsMessagesOfType(MessageType.Error))
            {
                EventManager.Events.Clear();
                return false;
            }

            bool isSuccess;
            try
            {
                // save the application to the database
                isSuccess = await DB.ApplicationTable.CreateApplicationAsync(Application);

                if (isSuccess)
                {
                    // update submitter code with last control code used
                    await UpdateSubmitterDefaultControlCodeAsync();

                    // save the events for the newly created application
                    await EventManager.SaveEventsAsync();

                    // update messages for display in UI
                    if (Application.Medium_Cd != "FTP") Application.Messages.AddInformation($"{LanguageResource.APPLICATION_REFERENCE_NUMBER}: {Appl_EnfSrv_Cd}-{Application.Subm_SubmCd}-{Appl_CtrlCd}");
                    if (Application.Medium_Cd != "FTP") Application.Messages.AddInformation(ReferenceData.Instance().ApplicationLifeStates[Application.AppLiSt_Cd].Description);

                    await DB.SubmitterTable.SubmitterMessageDeleteAsync(Application.Subm_SubmCd);
                }

            }
            catch (Exception e)
            {

                Application.Messages.AddError(e.Message);
                isSuccess = false;

            }

            if ((!isSuccess) && (Validation.IsSystemGeneratedControlCode))
                Application.Appl_CtrlCd = string.Empty; // reset to empty since it could not be saved

            return isSuccess;
        }

        public virtual async Task UpdateApplicationNoValidationAsync()
        {
            await DB.ApplicationTable.UpdateApplicationAsync(Application);
            await DB.SubmitterTable.SubmitterMessageDeleteAsync(Application.Appl_LastUpdate_Usr);

            await EventManager.SaveEventsAsync();
        }

        public virtual async Task UpdateApplicationAsync()
        {

            if (!(await ApplicationExistsAsync()))
            {
                Application.Messages.AddError(ErrorResource.CANT_UPDATE_APPLICATION_DOES_NOT_EXISTS);
                return;
            }

            // load old data from database
            var current = new ApplicationManager(new ApplicationData(), DB, config)
            {
                CurrentUser = this.CurrentUser
            };

            await current.LoadApplicationAsync(Appl_EnfSrv_Cd, Appl_CtrlCd);

            bool isCancelled = current.Application.ActvSt_Cd == "X";
            bool isReset = current.Application.AppLiSt_Cd.In(ApplicationState.INVALID_APPLICATION_1, ApplicationState.SIN_NOT_CONFIRMED_5);

            if (isReset && isCancelled)
            {
                Application.Messages.AddError(EventCode.C50527_CHANGES_NOT_ALLOWED_ON_CANCELLED_APPLICATION);
                return;
            }

            if (isReset) // reset
            {
                // clean data
                TrimSpaces();
                MakeUpperCase();

                await Process_00_InitialState();
            }
            else // regular update
            {
                Application.AppLiSt_Cd = current.Application.AppLiSt_Cd;
                Application.ActvSt_Cd = current.Application.ActvSt_Cd;

                Validation.ValidateAndRevertNonUpdateFields(current.Application);

                // update reason text with message

                string reasonText = BuildApplicationReasonText(BuildReferenceNumberChangeReasonText(Application, current.Application),
                                                               BuildAddressChangeReasonText(Application, current.Application),
                                                               BuildCommentsChangeReasonText(Application, current.Application));
                if (!string.IsNullOrEmpty(reasonText))
                    EventManager.AddEvent(EventCode.C51020_APPLICATION_UPDATED, reasonText);

            }

            if (Application.Messages.ContainsMessagesOfType(MessageType.Error))
                return;

            try
            {
                // save the application to the database
                await DB.ApplicationTable.UpdateApplicationAsync(Application);

                // update submitter code with last control code used
                await UpdateSubmitterDefaultControlCodeAsync();

                // save the events for the newly created application
                await EventManager.SaveEventsAsync();

                // update messages for display in UI
                if (Application.Medium_Cd != "FTP") Application.Messages.AddInformation($"{LanguageResource.APPLICATION_REFERENCE_NUMBER}: {Application.Appl_EnfSrv_Cd}-{Application.Subm_SubmCd}-{Application.Appl_CtrlCd}");
                if (Application.Medium_Cd != "FTP") Application.Messages.AddInformation(ReferenceData.Instance().ApplicationLifeStates[Application.AppLiSt_Cd].Description);

            }
            catch (Exception e)
            {
                Application.Messages.AddError(e.Message);
            }

        }

        public async Task TransferApplicationAsync(string applSelectedSubmitter, string applSelectedRecipient)
        {

            Application.Subm_SubmCd = applSelectedSubmitter;
            Application.Appl_LastUpdate_Usr = DB.CurrentSubmitter;
            if (Application.Medium_Cd == "ONL")
                Application.Subm_Recpt_SubmCd = applSelectedRecipient;

            var caseManagementData = new CaseManagementData
            {
                Appl_CtrlCd = Application.Appl_CtrlCd,
                Appl_EnfSrv_Cd = Application.Appl_EnfSrv_Cd,
                CaseMgmnt_Dte = DateTime.Now,
                Subm_Recpt_SubmCd = applSelectedRecipient,
                Subm_SubmCd = applSelectedSubmitter
            };
            await DB.CaseManagementTable.CreateCaseManagementAsync(caseManagementData);

            EventManager.AddEvent(EventCode.C51202_APPLICATION_HAS_BEEN_TRANSFERED);

            Application.Messages.AddInformation(EventCode.C51202_APPLICATION_HAS_BEEN_TRANSFERED);

            await UpdateApplicationAsync();

        }

        public async Task<List<StatsOutgoingProvincialData>> GetProvincialStatsOutgoingDataAsync(int maxRecords,
                                                                     string activeState,
                                                                     string recipientCode,
                                                                     bool isXML = true)
        {
            var applicationDB = DB.ApplicationTable;
            var data = await applicationDB.GetStatsProvincialOutgoingDataAsync(maxRecords, activeState, recipientCode, isXML);
            return data;
        }

        public virtual Task ProcessBringForwardsAsync(ApplicationEventData bfEvent)
        {
            throw new NotImplementedException("ProcessBringForwards has not been implemented!");
        }

        private static string BuildApplicationReasonText(string referenceNumberText, string addressText, string commentText)
        {

            var reasonText = new StringBuilder();

            if (!string.IsNullOrEmpty(referenceNumberText))
                reasonText.Append(referenceNumberText.Trim() + ", ");
            if (!string.IsNullOrEmpty(addressText))
                reasonText.Append(addressText.Trim() + ", ");
            if (!string.IsNullOrEmpty(commentText))
                reasonText.Append(commentText.Trim());

            return reasonText.ToString().TrimEnd(',', ' ');

        }

        protected async Task UpdateSubmitterDefaultControlCodeAsync()
        {
            await DB.ApplicationTable.UpdateSubmitterDefaultControlCodeAsync(Application.Subm_SubmCd, Application.Appl_CtrlCd);
        }

        protected virtual void TrimSpaces()
        {
            Application.Appl_EnfSrv_Cd = Application.Appl_EnfSrv_Cd?.Trim();
            Application.Appl_CtrlCd = Application.Appl_CtrlCd?.Trim();
            Application.Subm_SubmCd = Application.Subm_SubmCd?.Trim();
            Application.Subm_Recpt_SubmCd = Application.Subm_Recpt_SubmCd?.Trim();
            Application.Appl_CommSubm_Text = Application.Appl_CommSubm_Text?.Trim();
            Application.Appl_Group_Batch_Cd = Application.Appl_Group_Batch_Cd?.Trim();
            Application.Appl_Source_RfrNr = Application.Appl_Source_RfrNr?.Trim();
            Application.Subm_Affdvt_SubmCd = Application.Subm_Affdvt_SubmCd?.Trim();
            Application.Appl_Affdvt_DocTypCd = Application.Appl_Affdvt_DocTypCd?.Trim();
            Application.Appl_Crdtr_FrstNme = Application.Appl_Crdtr_FrstNme?.Trim();
            Application.Appl_Crdtr_MddleNme = Application.Appl_Crdtr_MddleNme?.Trim();
            Application.Appl_Crdtr_SurNme = Application.Appl_Crdtr_SurNme?.Trim();
            Application.Appl_Dbtr_FrstNme = Application.Appl_Dbtr_FrstNme?.Trim();
            Application.Appl_Dbtr_MddleNme = Application.Appl_Dbtr_MddleNme?.Trim();
            Application.Appl_Dbtr_SurNme = Application.Appl_Dbtr_SurNme?.Trim();
            Application.Appl_Dbtr_Parent_SurNme_Birth = Application.Appl_Dbtr_Parent_SurNme_Birth?.Trim();
            Application.Appl_Dbtr_LngCd = Application.Appl_Dbtr_LngCd?.Trim();
            Application.Appl_Dbtr_Gendr_Cd = Application.Appl_Dbtr_Gendr_Cd?.Trim();
            Application.Appl_Dbtr_Addr_Ln = Application.Appl_Dbtr_Addr_Ln?.Trim();
            Application.Appl_Dbtr_Addr_Ln1 = Application.Appl_Dbtr_Addr_Ln1?.Trim();
            Application.Appl_Dbtr_Addr_CityNme = Application.Appl_Dbtr_Addr_CityNme?.Trim();
            Application.Appl_Dbtr_Addr_PrvCd = Application.Appl_Dbtr_Addr_PrvCd?.Trim();
            Application.Appl_Dbtr_Addr_CtryCd = Application.Appl_Dbtr_Addr_CtryCd?.Trim();
            Application.Appl_Dbtr_Addr_PCd = Application.Appl_Dbtr_Addr_PCd?.Trim();
            Application.Medium_Cd = Application.Medium_Cd?.Trim();
            Application.AppCtgy_Cd = Application.AppCtgy_Cd?.Trim();
            Application.AppReas_Cd = Application.AppReas_Cd?.Trim();
            Application.ActvSt_Cd = Application.ActvSt_Cd?.Trim();
        }

        public virtual void MakeUpperCase()
        {
            Application.Appl_EnfSrv_Cd = Application.Appl_EnfSrv_Cd?.ToUpper();
            Application.Appl_CtrlCd = Application.Appl_CtrlCd?.ToUpper();
            Application.Subm_SubmCd = Application.Subm_SubmCd?.ToUpper();
            Application.Subm_Recpt_SubmCd = Application.Subm_Recpt_SubmCd?.ToUpper();
            Application.Appl_CommSubm_Text = Application.Appl_CommSubm_Text?.ToUpper();
            Application.Appl_Group_Batch_Cd = Application.Appl_Group_Batch_Cd?.ToUpper();
            Application.Appl_Source_RfrNr = Application.Appl_Source_RfrNr?.ToUpper();
            Application.Subm_Affdvt_SubmCd = Application.Subm_Affdvt_SubmCd?.ToUpper();
            Application.Appl_Affdvt_DocTypCd = Application.Appl_Affdvt_DocTypCd?.ToUpper();
            Application.Appl_Crdtr_FrstNme = Application.Appl_Crdtr_FrstNme?.ToUpper();
            Application.Appl_Crdtr_MddleNme = Application.Appl_Crdtr_MddleNme?.ToUpper();
            Application.Appl_Crdtr_SurNme = Application.Appl_Crdtr_SurNme?.ToUpper();
            Application.Appl_Dbtr_FrstNme = Application.Appl_Dbtr_FrstNme?.ToUpper();
            Application.Appl_Dbtr_MddleNme = Application.Appl_Dbtr_MddleNme?.ToUpper();
            Application.Appl_Dbtr_SurNme = Application.Appl_Dbtr_SurNme?.ToUpper();
            Application.Appl_Dbtr_Parent_SurNme_Birth = Application.Appl_Dbtr_Parent_SurNme_Birth?.ToUpper();
            Application.Appl_Dbtr_LngCd = Application.Appl_Dbtr_LngCd?.ToUpper();
            Application.Appl_Dbtr_Gendr_Cd = Application.Appl_Dbtr_Gendr_Cd?.ToUpper();
            Application.Appl_Dbtr_Addr_Ln = Application.Appl_Dbtr_Addr_Ln?.ToUpper();
            Application.Appl_Dbtr_Addr_Ln1 = Application.Appl_Dbtr_Addr_Ln1?.ToUpper();
            Application.Appl_Dbtr_Addr_CityNme = Application.Appl_Dbtr_Addr_CityNme?.ToUpper();
            Application.Appl_Dbtr_Addr_PrvCd = Application.Appl_Dbtr_Addr_PrvCd?.ToUpper();
            Application.Appl_Dbtr_Addr_CtryCd = Application.Appl_Dbtr_Addr_CtryCd?.ToUpper();
            Application.Appl_Dbtr_Addr_PCd = Application.Appl_Dbtr_Addr_PCd?.ToUpper();
            Application.Medium_Cd = Application.Medium_Cd?.ToUpper();
            Application.AppCtgy_Cd = Application.AppCtgy_Cd?.ToUpper();
            Application.AppReas_Cd = Application.AppReas_Cd?.ToUpper();
            Application.ActvSt_Cd = Application.ActvSt_Cd?.ToUpper();
        }

        public async Task<string> GetSINResultsEventTextAsync()
        {
            string result = string.Empty;

            var data = await DB.SINResultTable.GetSINResultsAsync(Application.Appl_EnfSrv_Cd, Application.Appl_CtrlCd);
            var sinData = data.Items.FirstOrDefault();

            if (sinData != null)
            {
                result = $"{sinData.SVR_DOB_TolCd}{sinData.SVR_GvnNme_TolCd}{sinData.SVR_MddlNme_TolCd}" +
                         $"{sinData.SVR_SurNme_TolCd}{sinData.SVR_MotherNme_TolCd}{sinData.SVR_Gendr_TolCd}";
                result += ((sinData.ValStat_Cd == 0) || (sinData.ValStat_Cd == 10)) ? "Y" : "N";
            }

            return result;
        }

        private static string BuildCommentsChangeReasonText(ApplicationData newAppl, ApplicationData currentAppl)
        {
            if (currentAppl.Appl_CommSubm_Text?.Trim() != newAppl.Appl_CommSubm_Text?.Trim())
            {
                if (currentAppl.Appl_EnfSrv_Cd == "QC01")
                    return "Commentaire";
                else
                    return "Comments";
            }
            else
                return "";
        }

        private static string BuildAddressChangeReasonText(ApplicationData newAppl, ApplicationData currentAppl)
        {
            if ((newAppl.Appl_Dbtr_Addr_Ln?.Trim() != currentAppl.Appl_Dbtr_Addr_Ln?.Trim()) ||
                (newAppl.Appl_Dbtr_Addr_Ln1?.Trim() != currentAppl.Appl_Dbtr_Addr_Ln1?.Trim()) ||
                (newAppl.Appl_Dbtr_Addr_CityNme?.Trim() != currentAppl.Appl_Dbtr_Addr_CityNme?.Trim()) ||
                (newAppl.Appl_Dbtr_Addr_CtryCd?.Trim() != currentAppl.Appl_Dbtr_Addr_CtryCd?.Trim()) ||
                (newAppl.Appl_Dbtr_Addr_PrvCd?.Trim() != currentAppl.Appl_Dbtr_Addr_PrvCd?.Trim()) ||
                (newAppl.Appl_Dbtr_Addr_PCd?.Trim() != currentAppl.Appl_Dbtr_Addr_PCd?.Trim()))
            {
                if (currentAppl.Appl_EnfSrv_Cd == "QC01")
                    return "Adresse du débiteur";
                else
                    return "Debtor Address";
            }
            else
                return "";
        }

        private static string BuildReferenceNumberChangeReasonText(ApplicationData newAppl, ApplicationData currentAppl)
        {
            if (currentAppl.Appl_Source_RfrNr?.Trim() != newAppl.Appl_Source_RfrNr?.Trim())
            {
                if (currentAppl.Appl_EnfSrv_Cd.Trim() == "QC01")
                    return "Numéro référence de l'autorité provinciale";
                else
                    return "Enforcement Service Reference Number";
            }
            else
                return "";
        }

        public static async Task AddSystemErrorAsync(IRepositories repositories, MessageDataList messages, string recipients, string errorMessage)
        {
            messages.AddSystemError(errorMessage);

            string subject = "System Error";
            string message = $"<b>Error: </b>{errorMessage}";
            await repositories.NotificationService.SendEmailAsync(subject, recipients, message);
        }

        private bool CurrentUserHasReadAccess(string enfService, string subm_SubmCd)
        {
            bool canAccess = true;

            if ((CurrentUser.HasRole(Roles.EnforcementService) ||
                 CurrentUser.HasRole(Roles.EnforcementServiceReadOnly) ||
                 CurrentUser.HasRole(Roles.FileTransfer)) &&
                !CurrentUser.IsSameEnfService(enfService))
                canAccess = false;

            if ((CurrentUser.HasRole(Roles.EnforcementOffice) ||
                 CurrentUser.HasRole(Roles.EnforcementOfficeReadOnly)) &&
                (!CurrentUser.IsSameEnfService(enfService) ||
                 !CurrentUser.IsSameOffice(subm_SubmCd)))
                canAccess = false;

            if (CurrentUser.HasRole(Roles.CourtUser) &&
                !CurrentUser.IsSameEnfService(enfService))
                canAccess = false;

            return canAccess;
        }

        private bool CurrentUserHasFullAccess(string enfService, string subm_SubmCd)
        {
            bool canAccess = true;

            if ((CurrentUser.HasRole(Roles.EnforcementService) ||
                 CurrentUser.HasRole(Roles.FileTransfer)) &&
                !CurrentUser.IsSameEnfService(enfService))
                canAccess = false;

            if (CurrentUser.HasRole(Roles.EnforcementOffice) &&
                (!CurrentUser.IsSameEnfService(enfService) ||
                 !CurrentUser.IsSameOffice(subm_SubmCd)))
                canAccess = false;

            if (CurrentUser.HasRole(Roles.CourtUser) &&
                !CurrentUser.IsSameEnfService(enfService))
                canAccess = false;

            if (CurrentUser.HasRole(Roles.EnforcementServiceReadOnly) ||
                CurrentUser.HasRole(Roles.EnforcementOfficeReadOnly))
                canAccess = false;

            return canAccess;
        }

    }
}
