// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    public class ValidationRule
    {
        private Func<bool> _checkFunc;
        private ValidationRule[] _prereqs;

        public ValidationRule(string errorMessage, Func<bool> checkFunc) : this(errorMessage, checkFunc, null) { }

        public ValidationRule(string errorMessage, Func<bool> checkFunc, params ValidationRule[] prerequisiteValidations)
        {
            ErrorMessage = errorMessage;
            _checkFunc = checkFunc;
            _prereqs = prerequisiteValidations;
        }

        public string ErrorMessage { get; private set; }

        public bool CheckPrerequisites()
        {
            return _prereqs == null || _prereqs.All(v => v.Check());
        }

        public bool Check()
        {
            return CheckPrerequisites() && _checkFunc();
        }

        public void CheckThrowing()
        {
            if (!Check())
            {
                throw new BadInputFormatException(ErrorMessage);
            }
        }
    }
}
