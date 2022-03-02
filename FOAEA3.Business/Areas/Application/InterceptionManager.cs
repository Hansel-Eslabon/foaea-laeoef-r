﻿using BackendProcesses.Business;
using FOAEA3.Model;
using FOAEA3.Model.Enums;
using FOAEA3.Model.Interfaces;
using FOAEA3.Resources.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FOAEA3.Business.Areas.Application
{
    internal partial class InterceptionManager : ApplicationManager
    {
        private const string I01_AFFITDAVIT_DOCUMENT_CODE = "IXX";

        public InterceptionApplicationData InterceptionApplication { get; }
        private IRepositories_Finance RepositoriesFinance { get; }
        private InterceptionValidation InterceptionValidation { get; }
        private bool? AcceptedWithin30Days { get; set; } = null;
        private bool ESDReceived { get; set; } = true;

        private int nextJusticeID_callCount = 0;

        private enum VariationDocumentAction
        {
            AcceptVariationDocument,
            RejectVariationDocument
        }

        private VariationDocumentAction VariationAction { get; set; }

        public InterceptionManager(InterceptionApplicationData interception, IRepositories repositories,
                                   IRepositories_Finance repositoriesFinance, CustomConfig config) :
                                  base(interception, repositories, config, new InterceptionValidation(interception, repositories, config))
        {
            InterceptionApplication = interception;
            InterceptionValidation = Validation as InterceptionValidation;
            RepositoriesFinance = repositoriesFinance;

            // add Interception-specific state changes

            StateEngine.ValidStateChange[ApplicationState.APPLICATION_ACCEPTED_10].Add(ApplicationState.FINANCIAL_TERMS_VARIED_17);
            StateEngine.ValidStateChange[ApplicationState.APPLICATION_ACCEPTED_10].Add(ApplicationState.APPLICATION_SUSPENDED_35);

            StateEngine.ValidStateChange[ApplicationState.PARTIALLY_SERVICED_12].Add(ApplicationState.FINANCIAL_TERMS_VARIED_17);
            StateEngine.ValidStateChange[ApplicationState.PARTIALLY_SERVICED_12].Add(ApplicationState.APPLICATION_SUSPENDED_35);

            StateEngine.ValidStateChange.Add(ApplicationState.FINANCIAL_TERMS_VARIED_17, new List<ApplicationState> {
                            ApplicationState.INVALID_VARIATION_SOURCE_91,
                            ApplicationState.INVALID_VARIATION_FINTERMS_92,
                            ApplicationState.VALID_FINANCIAL_VARIATION_93
                        });

            StateEngine.ValidStateChange.Add(ApplicationState.AWAITING_DOCUMENTS_FOR_VARIATION_19, new List<ApplicationState> {
                            ApplicationState.PARTIALLY_SERVICED_12,
                            ApplicationState.MANUALLY_TERMINATED_14,
                            ApplicationState.EXPIRED_15,
                            ApplicationState.APPLICATION_SUSPENDED_35
                        });

            StateEngine.ValidStateChange.Add(ApplicationState.APPLICATION_SUSPENDED_35, new List<ApplicationState> {
                            ApplicationState.MANUALLY_TERMINATED_14,
                            ApplicationState.EXPIRED_15,
                            ApplicationState.FINANCIAL_TERMS_VARIED_17
                        });

            StateEngine.ValidStateChange.Add(ApplicationState.INVALID_VARIATION_SOURCE_91, new List<ApplicationState> {
                            ApplicationState.VALID_FINANCIAL_VARIATION_93
                        });

            StateEngine.ValidStateChange.Add(ApplicationState.INVALID_VARIATION_FINTERMS_92, new List<ApplicationState> {
                            ApplicationState.VALID_FINANCIAL_VARIATION_93
                        });

            StateEngine.ValidStateChange.Add(ApplicationState.VALID_FINANCIAL_VARIATION_93, new List<ApplicationState> {
                            ApplicationState.PARTIALLY_SERVICED_12,
                            ApplicationState.AWAITING_DOCUMENTS_FOR_VARIATION_19
                        });
        }

        public InterceptionManager(IRepositories repositories, IRepositories_Finance repositoriesFinance, CustomConfig config) :
            this(new InterceptionApplicationData(), repositories, repositoriesFinance, config)
        {

        }

        public bool LoadApplication(string enfService, string controlCode, bool loadFinancials)
        {
            bool isSuccess = base.LoadApplication(enfService, controlCode);

            if (isSuccess && loadFinancials)
            {
                var finTerms = Repositories.InterceptionRepository.GetInterceptionFinancialTerms(enfService, controlCode);
                InterceptionApplication.IntFinH = finTerms;

                if (finTerms != null)
                {
                    var holdbackConditions = Repositories.InterceptionRepository.GetHoldbackConditions(enfService, controlCode,
                                                                                                       finTerms.IntFinH_Dte);

                    InterceptionApplication.HldbCnd = holdbackConditions;
                }
            }

            return isSuccess;
        }

        public override bool LoadApplication(string enfService, string controlCode)
        {
            // get data from Appl
            return LoadApplication(enfService, controlCode, loadFinancials: true);
        }

        public override bool CreateApplication()
        {
            if (!IsValidCategory("I01"))
                return false;

            if (InterceptionApplication.IntFinH is null)
            {
                InterceptionApplication.Messages.AddError("Missing financial terms");
                return false;
            }

            bool success = base.CreateApplication();

            if (success)
            {
                InterceptionApplication.IntFinH.ActvSt_Cd = "P";
                InterceptionApplication.IntFinH.IntFinH_VarIss_Dte = null;

                foreach (var sourceSpecificHoldback in InterceptionApplication.HldbCnd)
                    sourceSpecificHoldback.ActvSt_Cd = "P";

                if (InterceptionApplication.IntFinH.IntFinH_PerPym_Money.HasValue &&
                    (InterceptionApplication.IntFinH.IntFinH_PerPym_Money.Value == 0))
                    InterceptionApplication.IntFinH.IntFinH_PerPym_Money = null;

                if (InterceptionApplication.IntFinH.IntFinH_CmlPrPym_Ind.HasValue &&
                    (InterceptionApplication.IntFinH.IntFinH_CmlPrPym_Ind.Value != 1))
                    EventManager.AddEvent(EventCode.C51114_FINANCIAL_TERMS_INCLUDE_NONCUMULATIVE_PERIODIC_PAYMENTS);

                Repositories.InterceptionRepository.CreateInterceptionFinancialTerms(InterceptionApplication.IntFinH);

                Repositories.InterceptionRepository.CreateHoldbackConditions(InterceptionApplication.HldbCnd);

                InterceptionApplication.Messages.AddInformation(EventCode.C50620_VALID_APPLICATION);

                IncrementGarnSmry(isNewApplication: true);

                if (config.ESDsites.Contains(Appl_EnfSrv_Cd))
                    Repositories.InterceptionRepository.InsertESDrequired(Appl_EnfSrv_Cd, Appl_CtrlCd, ESDrequired.OriginalESDrequired);

                EventManager.SaveEvents();

            }

            return success;
        }

        public override void UpdateApplication()
        {
            base.UpdateApplication();

            Repositories.InterceptionRepository.UpdateInterceptionFinancialTerms(InterceptionApplication.IntFinH);

            Repositories.InterceptionRepository.UpdateHoldbackConditions(InterceptionApplication.HldbCnd);

        }

        public override void UpdateApplicationNoValidation()
        {
            base.UpdateApplicationNoValidation();

            Repositories.InterceptionRepository.UpdateInterceptionFinancialTerms(InterceptionApplication.IntFinH);

            Repositories.InterceptionRepository.UpdateHoldbackConditions(InterceptionApplication.HldbCnd);

        }

        public bool VaryApplication()
        {
            if (!IsValidCategory("I01"))
                return false;

            // ignore passed core information -- keep only new financials
            var applicationManagerCopy = new InterceptionManager(Repositories, RepositoriesFinance, config);

            if (!applicationManagerCopy.LoadApplication(Appl_EnfSrv_Cd, Appl_CtrlCd, loadFinancials: false))
            {
                EventManager.AddEvent(EventCode.C55000_INVALID_VARIATION);
                EventManager.SaveEvents();

                return false;
            }

            var summSmry = RepositoriesFinance.SummonsSummaryRepository.GetSummonsSummary(Appl_EnfSrv_Cd, Appl_CtrlCd).FirstOrDefault();
            if (summSmry.Start_Dte >= DateTime.Now)
            {
                EventManager.AddEvent(EventCode.C50881_CANNOT_VARY_TERMS_AT_THIS_TIME);
                EventManager.SaveEvents();

                return false;
            }

            if (!InterceptionValidation.ValidNewFinancialTerms(InterceptionApplication))
                return false;

            InterceptionApplication.IntFinH.ActvSt_Cd = "P";
            InterceptionApplication.IntFinH.IntFinH_LiStCd = 17;
            InterceptionApplication.IntFinH.Appl_CtrlCd = Appl_CtrlCd;
            InterceptionApplication.IntFinH.Appl_EnfSrv_Cd = Appl_EnfSrv_Cd;

            foreach (var sourceSpecific in InterceptionApplication.HldbCnd)
            {
                sourceSpecific.ActvSt_Cd = "P";
                sourceSpecific.HldbCnd_LiStCd = 17;
                sourceSpecific.Appl_CtrlCd = Appl_CtrlCd;
                sourceSpecific.Appl_EnfSrv_Cd = Appl_EnfSrv_Cd;
            }

            if (InterceptionApplication.IntFinH.IntFinH_CmlPrPym_Ind.HasValue &&
                (InterceptionApplication.IntFinH.IntFinH_CmlPrPym_Ind.Value != 1))
            {
                EventManager.AddEvent(EventCode.C51114_FINANCIAL_TERMS_INCLUDE_NONCUMULATIVE_PERIODIC_PAYMENTS);
            }

            SetNewStateTo(ApplicationState.FINANCIAL_TERMS_VARIED_17);

            if (InterceptionApplication.AppLiSt_Cd.NotIn(ApplicationState.INVALID_VARIATION_SOURCE_91,
                                                         ApplicationState.INVALID_VARIATION_FINTERMS_92))
            {
                UpdateApplicationNoValidation();

                EventManager.SaveEvents();

                InterceptionApplication.Messages.AddInformation(EventCode.C50620_VALID_APPLICATION);

                return true;
            }
            else
            {
                switch (InterceptionApplication.AppLiSt_Cd)
                {
                    case ApplicationState.INVALID_VARIATION_SOURCE_91:
                        InterceptionApplication.Messages.AddError(EventCode.C55002_INVALID_FINANCIAL_TERMS);
                        break;

                    case ApplicationState.INVALID_VARIATION_FINTERMS_92:
                        InterceptionApplication.Messages.AddError(EventCode.C55001_INVALID_SOURCE_HOLDBACK);
                        break;

                    default:
                        InterceptionApplication.Messages.AddError(EventCode.C55000_INVALID_VARIATION);
                        break;
                }

                // don't update the application in the database, but only save the events

                EventManager.SaveEvents();

                return false;
            }

        }

        public void FullyServiceApplication()
        {
            var applicationManagerCopy = new InterceptionManager(Repositories, RepositoriesFinance, config);

            if (!applicationManagerCopy.LoadApplication(Appl_EnfSrv_Cd, Appl_CtrlCd, loadFinancials: false))
            {
                // TODO: generate error that application does not exists
                return;
            }

            SetNewStateTo(ApplicationState.FULLY_SERVICED_13);

            UpdateApplicationNoValidation();

            EventManager.SaveEvents();
        }

        public bool CancelApplication()
        {
            if (!IsValidCategory("I01"))
                return false;

            var applicationManagerCopy = new InterceptionManager(Repositories, RepositoriesFinance, config);
            if (!applicationManagerCopy.LoadApplication(Appl_EnfSrv_Cd, Appl_CtrlCd))
            {
                InterceptionApplication.Messages.AddError($"No application was found in the database for {Appl_EnfSrv_Cd}-{Appl_CtrlCd}");
                return false;
            }

            InterceptionApplication.Appl_LastUpdate_Usr = Repositories.CurrentSubmitter;
            InterceptionApplication.Appl_LastUpdate_Dte = DateTime.Now;

            var interceptionCurrentlyInDB = applicationManagerCopy.InterceptionApplication;

            InterceptionApplication.Appl_Dbtr_Addr_Ln = interceptionCurrentlyInDB.Appl_Dbtr_Addr_Ln;
            InterceptionApplication.Appl_Dbtr_Addr_Ln1 = interceptionCurrentlyInDB.Appl_Dbtr_Addr_Ln1;
            InterceptionApplication.Appl_Dbtr_Addr_PCd = interceptionCurrentlyInDB.Appl_Dbtr_Addr_PCd;
            InterceptionApplication.Appl_Dbtr_Addr_PrvCd = interceptionCurrentlyInDB.Appl_Dbtr_Addr_PrvCd;
            InterceptionApplication.Appl_Dbtr_Addr_CityNme = interceptionCurrentlyInDB.Appl_Dbtr_Addr_CityNme;
            InterceptionApplication.Appl_Dbtr_Addr_CtryCd = interceptionCurrentlyInDB.Appl_Dbtr_Addr_CtryCd;

            if (interceptionCurrentlyInDB.ActvSt_Cd != "A")
            {
                EventManager.AddEvent(EventCode.C50841_CAN_ONLY_CANCEL_AN_ACTIVE_APPLICATION);
                return false;
            }

            SetNewStateTo(ApplicationState.MANUALLY_TERMINATED_14);

            UpdateApplicationNoValidation();

            EventManager.SaveEvents();

            InterceptionApplication.Messages.AddInformation(EventCode.C50620_VALID_APPLICATION);

            return true;
        }

        public bool AcceptGarnishee(bool isAutoAccept = false)
        {
            if (!IsValidCategory("I01"))
                return false;
            
            var applicationManagerCopy = new InterceptionManager(Repositories, RepositoriesFinance, config);

            // ignore passed core information -- keep only new financials

            if (!applicationManagerCopy.LoadApplication(Appl_EnfSrv_Cd, Appl_CtrlCd, loadFinancials: false))
            {
                return false;
            }

            var interceptionCurrentlyInDB = applicationManagerCopy.InterceptionApplication;

            AcceptedWithin30Days = true;

            if (!isAutoAccept)
            {
                var interceptionDB = Repositories.InterceptionRepository;

                bool isESDsite = IsESD_MEP(Appl_EnfSrv_Cd);
                DateTime garnisheeSummonsReceiptDate = interceptionDB.GetGarnisheeSummonsReceiptDate(Appl_EnfSrv_Cd, Appl_CtrlCd, isESDsite);

                var dateDiff = garnisheeSummonsReceiptDate - interceptionCurrentlyInDB.Appl_Lgl_Dte;
                if (dateDiff.HasValue && dateDiff.Value.Days > 30)
                {
                    AcceptedWithin30Days = false;
                    RejectInterception();

                    return false;
                }

                if (string.IsNullOrEmpty(InterceptionApplication.Appl_CommSubm_Text))
                    InterceptionApplication.Appl_CommSubm_Text = interceptionCurrentlyInDB.Appl_CommSubm_Text;

                InterceptionApplication.Appl_LastUpdate_Usr = Repositories.CurrentSubmitter;
                InterceptionApplication.Appl_LastUpdate_Dte = DateTime.Now;

                SetNewStateTo(ApplicationState.VALID_AFFIDAVIT_NOT_RECEIVED_7);

                InterceptionApplication.Appl_Rcptfrm_Dte = garnisheeSummonsReceiptDate;

                UpdateApplicationNoValidation();

                EventManager.SaveEvents();

                InterceptionApplication.Messages.AddInformation(EventCode.C50620_VALID_APPLICATION);

            }

            return true;

        }

        public bool AcceptVariationDocument(string comments)
        {
            if (!IsValidCategory("I01"))
                return false;
            
            var applicationManagerCopy = new InterceptionManager(Repositories, RepositoriesFinance, config);

            if (!applicationManagerCopy.LoadApplication(Appl_EnfSrv_Cd, Appl_CtrlCd, loadFinancials: false))
            {
                InterceptionApplication.Messages.AddError($"No application was found in the database for {Appl_EnfSrv_Cd}-{Appl_CtrlCd}");
                return false;
            }

            var interceptionCurrentlyInDB = applicationManagerCopy.InterceptionApplication;

            if (interceptionCurrentlyInDB.AppLiSt_Cd != ApplicationState.AWAITING_DOCUMENTS_FOR_VARIATION_19)
            {
                InvalidStateChange(interceptionCurrentlyInDB.AppLiSt_Cd, InterceptionApplication.AppLiSt_Cd);

                EventManager.SaveEvents();

                return false;
            }

            if (!string.IsNullOrEmpty(comments.Trim()))
                InterceptionApplication.Appl_CommSubm_Text = comments.Trim();

            InterceptionApplication.Appl_LastUpdate_Usr = Repositories.CurrentSubmitter;
            InterceptionApplication.Appl_LastUpdate_Dte = DateTime.Now;

            EventManager.AddEvent(EventCode.C51111_VARIATION_ACCEPTED);

            ChangeStateForFinancialTerms(oldState: "A", newState: "I", 12);
            ChangeStateForFinancialTerms(oldState: "P", newState: "A", 12);

            var activeFinTerms = Repositories.InterceptionRepository.GetInterceptionFinancialTerms(Appl_EnfSrv_Cd, Appl_CtrlCd, "A");

            VariationAction = VariationDocumentAction.AcceptVariationDocument;

            SetNewStateTo(ApplicationState.PARTIALLY_SERVICED_12);

            // refresh the amount owed values in SummSmry

            var amountOwedProcess = new AmountOwedProcess(Repositories, RepositoriesFinance);
            var (summSmryNewData, _) = amountOwedProcess.CalculateAndUpdateAmountOwedForVariation(Appl_EnfSrv_Cd, Appl_CtrlCd);

            // update application

            decimal preBalance = summSmryNewData.PreBalance;

            Repositories.InterceptionRepository.InsertBalanceSnapshot(Appl_EnfSrv_Cd, Appl_CtrlCd, preBalance,
                                                                      BalanceSnapshotChangeType.VARIATION_ACCEPTED,
                                                                      intFinH_Date: activeFinTerms.IntFinH_Dte);
            UpdateApplicationNoValidation();

            if (!string.IsNullOrEmpty(activeFinTerms.IntFinH_DefHldbAmn_Period))
            {
                var fixedAmountData = RepositoriesFinance.SummonsSummaryFixedAmountRepository.GetSummonsSummaryFixedAmount(Appl_EnfSrv_Cd, Appl_CtrlCd);
                if (fixedAmountData is null)
                {
                    var newFixedAmountRecalcDateTime = RecalculateFixedAmountRecalcDateAfterVariation(activeFinTerms, DateTime.Now);
                    RepositoriesFinance.SummonsSummaryFixedAmountRepository.CreateSummonsSummaryFixedAmount(Appl_EnfSrv_Cd, Appl_CtrlCd,
                                                                                                            newFixedAmountRecalcDateTime);
                }
            }
            else
                RepositoriesFinance.SummonsSummaryFixedAmountRepository.DeleteSummSmryFixedAmountRecalcDate(Appl_EnfSrv_Cd, Appl_CtrlCd);

            EventManager.SaveEvents();

            InterceptionApplication.Messages.AddInformation(EventCode.C50620_VALID_APPLICATION);

            return true;
        }

        public void RejectInterception()
        {
            InterceptionApplication.Appl_LastUpdate_Usr = Repositories.CurrentSubmitter;
            InterceptionApplication.Appl_LastUpdate_Dte = DateTime.Now;

            SetNewStateTo(ApplicationState.APPLICATION_REJECTED_9);

            UpdateApplicationNoValidation();

            IncrementGarnSmry();

            EventManager.SaveEvents();

            InterceptionApplication.Messages.AddInformation(EventCode.C50620_VALID_APPLICATION);
        }
        
        public override void ProcessBringForwards(ApplicationEventData bfEvent)
        {
            bool closeEvent = false;

            TimeSpan diff;
            if (InterceptionApplication.Appl_LastUpdate_Dte.HasValue)
                diff = InterceptionApplication.Appl_LastUpdate_Dte.Value - DateTime.Now;
            else
                diff = TimeSpan.Zero;

            if ((InterceptionApplication.ActvSt_Cd != "A") &&
                ((!bfEvent.Event_Reas_Cd.HasValue) || (
                 (bfEvent.Event_Reas_Cd.NotIn(EventCode.C50806_SCHEDULED_TO_BE_REINSTATED__QUARTERLY_TRACING,
                                              EventCode.C50680_CHANGE_OR_SUPPLY_ADDITIONAL_DEBTOR_INFORMATION_SEE_SIN_VERIFICATION_RESULTS_PAGE_IN_FOAEA_FOR_SPECIFIC_DETAILS,
                                              EventCode.C50600_INVALID_APPLICATION)))) &&
                ((diff.Equals(TimeSpan.Zero)) || (Math.Abs(diff.TotalHours) > 24)))
            {
                bfEvent.AppLiSt_Cd = ApplicationState.MANUALLY_TERMINATED_14;
                bfEvent.ActvSt_Cd = "I";

                EventManager.SaveEvent(bfEvent);
            }
            else
            {
                if (bfEvent.Event_Reas_Cd.HasValue)
                {
                    var dbNotification = Repositories.NotificationRepository;
                    switch (bfEvent.Event_Reas_Cd)
                    {
                        case EventCode.C54005_CREATE_A_DEBTOR_LETTER_EVENT_IN_EVNTDBTR:
                            if (InterceptionApplication.Appl_Rcptfrm_Dte.AddDays(20) < DateTime.Now)
                            {
                                dbNotification.SendEmail("Debtor Letter create date exceeds 20 day range",
                                    "", // System.Configuration.ConfigurationManager.AppSettings("EmailRecipients")
                                    $"Debtor letter created for I01 Application {Appl_EnfSrv_Cd} {Appl_CtrlCd} more than 20 days after Garnishee Summons Receipt Date. \n" +
                                    $"Debtor name: {InterceptionApplication.Appl_Dbtr_SurNme}, {InterceptionApplication.Appl_Dbtr_FrstNme}\n" +
                                    $"Justice ID: {InterceptionApplication.Appl_JusticeNr}\n" +
                                    $"Garnishee Summons Receipt Date: {InterceptionApplication.Appl_Rcptfrm_Dte.ToShortDateString()}\n" +
                                    $"Debtor letter create date: {DateTime.Now.ToShortDateString()}"
                                    );
                            }
                            EventManager.AddEvent(EventCode.UNDEFINED, queue: EventQueue.EventDbtr); // ???
                            break;

                        case EventCode.C54001_BF_EVENT:
                        case EventCode.C54002_TELEPHONE_EVENT:
                            EventManager.AddEvent(bfEvent.Event_Reas_Cd.Value);
                            break;

                        default:
                            EventManager.AddEvent(EventCode.C54003_UNKNOWN_EVNTBF, queue: EventQueue.EventSYS);
                            break;
                    }
                }
            }

            if (closeEvent)
            {
                bfEvent.AppLiSt_Cd = InterceptionApplication.AppLiSt_Cd;
                bfEvent.ActvSt_Cd = "C";

                EventManager.SaveEvent(bfEvent);
            }

            EventManager.SaveEvents();
        }
    }
}
