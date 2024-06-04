// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Performance.SDK.Processing;

namespace Utilities
{
    public static class ProcessingSourceInfoGenerator
    {
        public static ProcessingSourceInfo GetEmpty()
        {
            return new ProcessingSourceInfo()
            {
                Owners = Array.Empty<ContactInfo>(),
                ProjectInfo = null,
                LicenseInfo = null,
                CopyrightNotice = null,
                AdditionalInformation = Array.Empty<string>(),
            };
        }
    }
}