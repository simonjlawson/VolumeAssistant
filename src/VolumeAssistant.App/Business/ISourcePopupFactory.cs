namespace VolumeAssistant.App.Business;

internal interface ISourcePopupFactory
{
    ISourcePopup Create(string text);
}
