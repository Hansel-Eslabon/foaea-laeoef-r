﻿using System;

namespace FOAEA3.Model.Exceptions
{
    [Serializable]
    public class ApplicationManagerException : Exception
    {
        public ApplicationManagerException()
        {
        }

        public ApplicationManagerException(string message) : base(message)
        {
        }

        public ApplicationManagerException(string message, Exception innerException) : base(message, innerException)
        {
        }

    }
}
