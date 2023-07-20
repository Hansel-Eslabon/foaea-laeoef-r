﻿using FOAEA3.Model;
using FOAEA3.Model.Interfaces;
using FOAEA3.Model.Interfaces.Broker;

namespace FOAEA3.Common.Brokers
{
    public class LicenceDenialEventAPIBroker : ILicenceDenialEventAPIBroker
    {
        public IAPIBrokerHelper ApiHelper { get; }
        public string Token { get; set; }

        public LicenceDenialEventAPIBroker(IAPIBrokerHelper apiHelper, string token)
        {
            ApiHelper = apiHelper;
            Token = token;
        }

        public async Task<List<ApplicationEventData>> GetRequestedLICINEvents(string enfSrvCd, string appl_EnfSrv_Cd, string appl_CtrlCd)
        {
            string apiCall = $"api/v1/licenceDenialEvents/RequestedLICIN?enforcementServiceCode={enfSrvCd}&" +
                             $"appl_EnfSrv_Cd={appl_EnfSrv_Cd}&appl_CtrlCd={appl_CtrlCd}";
            return await ApiHelper.GetData<List<ApplicationEventData>>(apiCall, token: Token);
        }

        public async Task<List<ApplicationEventDetailData>> GetRequestedLICINEventDetails(string enfSrvCd, string appl_EnfSrv_Cd, string appl_CtrlCd)
        {
            string apiCall = $"api/v1/licenceDenialEventDetails/RequestedLICIN?enforcementServiceCode={enfSrvCd}&" +
                             $"appl_EnfSrv_Cd={appl_EnfSrv_Cd}&appl_CtrlCd={appl_CtrlCd}";
            return await ApiHelper.GetData<List<ApplicationEventDetailData>>(apiCall, token: Token);
        }

    }
}
