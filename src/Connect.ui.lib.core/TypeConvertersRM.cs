using System;
using BookLibConnect.Aux;
using BookLibConnect.Aux.Extensions;

namespace BookLibConnect.Core.UI {
  class EnumConverterRM<TEnum> : EnumConverter<TEnum>
     where TEnum : struct, Enum {
    
    public EnumConverterRM () {
      ResourceManager = this.GetDefaultResourceManager ();
    }
  }
}
