using System;
using Oahu.Aux;
using Oahu.Aux.Extensions;

namespace Oahu.Core.UI {
  class EnumConverterRM<TEnum> : EnumConverter<TEnum>
     where TEnum : struct, Enum {
    
    public EnumConverterRM () {
      ResourceManager = this.GetDefaultResourceManager ();
    }
  }
}
