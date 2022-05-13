﻿using FOAEA3.Model.Interfaces;
using System;
using System.Collections.Generic;

namespace FOAEA3.Model
{
    public class InterceptionApplicationData : ApplicationData
    {
        public InterceptionFinancialHoldbackData IntFinH { get; set; }

        public List<HoldbackConditionData> HldbCnd { get; set; }

        public InterceptionApplicationData()
        {
            AppCtgy_Cd = "I01";
            ActvSt_Cd = "A";
            Appl_SIN_Cnfrmd_Ind = 0;
            Appl_Create_Dte = DateTime.Now;
            Appl_Rcptfrm_Dte = Appl_Create_Dte.Date; // only date, no time
            Appl_Lgl_Dte = Appl_Create_Dte;
            Appl_LastUpdate_Dte = Appl_Create_Dte;

            IntFinH = new InterceptionFinancialHoldbackData();
            HldbCnd = new List<HoldbackConditionData>();
        }

    }
}
