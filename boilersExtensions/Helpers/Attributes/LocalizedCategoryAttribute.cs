using System;
using System.ComponentModel;

namespace boilersExtensions.Helpers.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class LocalizedCategoryAttribute : CategoryAttribute
    {
        public LocalizedCategoryAttribute(string resourceKey) : base(ResourceService.GetString(resourceKey))
        {
        }
    }
}
