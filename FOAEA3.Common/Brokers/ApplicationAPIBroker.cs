﻿using FOAEA3.Model;
using FOAEA3.Model.Interfaces;
using FOAEA3.Model.Interfaces.Broker;
using FOAEA3.Resources.Helpers;

namespace FOAEA3.Common.Brokers
{
    public class ApplicationAPIBroker : IApplicationAPIBroker
    {
        private IAPIBrokerHelper ApiHelper { get; }

        public ApplicationAPIBroker(IAPIBrokerHelper apiHelper)
        {
            ApiHelper = apiHelper;
        }

        public ApplicationData GetApplication(string appl_EnfSrvCd, string appl_CtrlCd)
        {
            string key = ApplKey.MakeKey(appl_EnfSrvCd, appl_CtrlCd);
            string apiCall = $"api/v1/applications/{key}";
            return ApiHelper.GetDataAsync<ApplicationData>(apiCall).Result;
        }

        public ApplicationData SinConfirmation(string appl_EnfSrvCd, string appl_CtrlCd, SINConfirmationData confirmationData)
        {
            string key = ApplKey.MakeKey(appl_EnfSrvCd, appl_CtrlCd);
            string baseCall = "api/v1/Applications";
            string apiCall = $"{baseCall}/{key}/SinConfirmation";
            return ApiHelper.PutDataAsync<ApplicationData, SINConfirmationData>(apiCall, confirmationData).Result;
        }
    }
}