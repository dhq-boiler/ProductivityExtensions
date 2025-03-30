using System;
using System.ComponentModel;

namespace boilersExtensions.Helpers.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class LocalizedDescriptionAttribute : DescriptionAttribute
    {
        public LocalizedDescriptionAttribute(string resourceKey) : base(ResourceService.GetString(resourceKey))
        {
        }
    }
}