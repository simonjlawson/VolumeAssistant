using System;

namespace VolumeAssistant.App.Business;

internal sealed class DefaultSourcePopupFactory : ISourcePopupFactory
{
    public ISourcePopup Create(string text)
    {
        return new SourcePopup(text);
    }
}
