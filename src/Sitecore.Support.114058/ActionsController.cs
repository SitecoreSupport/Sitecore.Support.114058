namespace Sitecore.Support.ListManagement.Services
{
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Web;
  using System.Web.Http;
  using Data.Items;
  using Diagnostics;
  using Globalization;
  using Sitecore.ListManagement.ContentSearch.Model;
  using Sitecore.ListManagement.Services.Model;
  using Sitecore.Services.Core;
  using Sitecore.Services.Infrastructure.Web.Http;
  using Web.Http.Filters;
  using ContactData = Sitecore.ListManagement.ContentSearch.Model.ContactData;
  using Sitecore.ListManagement.Services;
  using Sitecore.ListManagement;
  using Texts = Sitecore.ListManagement.Services.Texts;
  using Sitecore;
  using Analytics.Data;
  using Abstractions;
  using Sitecore.ListManagement.ContentSearch;

  [ContactListLockedExceptionFilter]
  [AccessDeniedExceptionFilter]
  [UnauthorizedAccessExceptionFilter]
  [SitecoreAuthorize(Roles = Sitecore.ListManagement.Constants.SitecoreListManagerEditorsRole)]
  [AnalyticsDisabledAttributeFilter]
  [ServicesController("ListManagement.Actions")]
  public class ActionsController : ServicesApiController
  {
    private readonly ListManager<ContactList, ContactData> listManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionsController" /> class.
    /// </summary>
    public ActionsController()
      : this(RepositoryContainer<ContactList, ContactList, ContactListModel>.GetContactListManager())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionsController" /> class.
    /// </summary>
    /// <param name="listManager">The list manager.</param>
    public ActionsController(ListManager<ContactList, ContactData> listManager)
    {
      this.listManager = listManager;
    }

    /// <summary>
    /// Converts the list.
    /// </summary>
    /// <param name="id">The list identifier.</param>
    /// <returns>The string representation of the id of the created list.</returns>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public string ConvertList(string id)
    {
      return this.listManager.Convert(id).Id;
    }

    /// <summary>
    /// Removes all contact associations and sources.
    /// </summary>
    /// <param name="id">The list identifier.</param>
    /// <returns>The count of contacts that were removed.</returns>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public int RemoveAllContactAssociationsAndSources(string id)
    {
      return this.listManager.RemoveContactAssociations(id);
    }

    /// <summary>
    /// Removes the contact.
    /// </summary>
    /// <param name="id">Id of the list.</param>
    /// <param name="contactId">The contact identifier.</param>
    /// <returns>The number of contacts which have been removed.</returns>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public bool RemoveContact(string id, string contactId)
    {
      var list = this.listManager.FindById(id);
      Assert.IsNotNull(list, Texts.UnableToRemoveContact0FromList1TheListIsNotFound, contactId, id);

      var contacts = this.listManager.GetContacts(list).Where(c => c.ContactId == Guid.Parse(contactId)).ToArray();
      return this.listManager.RemoveContactAssociations(list, contacts) == 1;
    }

    /// <summary>
    /// Creates new contact and then adds it to the list.
    /// </summary>
    /// <param name="id">Id of the list.</param>
    /// <param name="contact">The contact to create.</param>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public HttpResponseMessage AddNewContact(string id, ContactData contact)
    {
      var model = new AddNewContactModel
      {
        FirstName = contact.FirstName,
        Surname = contact.Surname,
        Identifier = contact.Identifier,
        PreferredEmail = contact.PreferredEmail
      };

      this.Validate(model);

      if (!this.ModelState.IsValid)
      {
        throw new HttpResponseException(this.ActionContext.Request.CreateErrorResponse(HttpStatusCode.BadRequest, this.ModelState));
      }

      var list = this.listManager.FindById(id);

      if (list == null)
      {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
          ReasonPhrase = Translate.Text(Texts.UnableToAddContact0ToList1TheListIsNotFound, contact.Identifier, id)
        };
      }

      if (this.listManager.IsLocked(list) || this.listManager.IsInUse(list))
      {
        return new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
          ReasonPhrase = Translate.Text(Texts.AtTheMomentYouCannotUseOrEditTheListAnotherUserIsUsingTheListOrItsSourcePleaseTryAgainLater)
        };
      }

      this.listManager.AssociateContacts(list, new[] { contact });

      return new HttpResponseMessage(HttpStatusCode.Accepted)
      {
        ReasonPhrase = Translate.Text(Texts.TheContactHasBeenAddedToTheListAndWillBeAvailableAfterIndexing)
      };
    }

    /// <summary>
    /// The delete list by id.
    /// </summary>
    /// <param name="id">The list id.</param>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public void DeleteListById(string id)
    {
      this.listManager.Delete(id);
    }

    /// <summary>
    /// Unlocks the list.
    /// </summary>
    /// <param name="id">The list identifier.</param>
    [SitecoreAuthorize(AdminsOnly = true)]
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public void UnlockList(string id)
    {
      var list = this.listManager.FindById(id);

      if (list == null)
      {
        return;
      }

      var lockContext = this.listManager.GetLock(list);

      if (lockContext == null)
      {
        return;
      }

      this.listManager.Unlock(lockContext);
    }

    /// <summary>
    /// Renames the folder.
    /// </summary>
    /// <param name="id">The folder identifier.</param>
    /// <param name="newName">The folder name.</param>
    /// <returns>The result of folder renaming.</returns>
    [HttpPost]
    [ContactListLockedExceptionFilter(Texts.UnableToDeleteOrEditFolderOneOrMoreOfTheListsContainedWithinOrTheirSourcesAreInUse)]
    [ValidateHttpAntiForgeryToken]
    public HttpResponseMessage RenameFolder(string id, string newName)
    {
      newName = HttpUtility.UrlDecode(newName);
      if (!string.IsNullOrEmpty(newName) && ItemUtil.IsItemNameValid(newName))
      {
        this.listManager.RenameFolder(id, newName);
        return base.Request.CreateResponse(HttpStatusCode.Created);
      }

      return this.Request.CreateErrorResponse(HttpStatusCode.BadRequest, Texts.InvalidFolderNamePleaseUsePermittedCharacters);
    }

    /// <summary>
    /// The move folder.
    /// </summary>
    /// <param name="id">The folder id.</param>
    /// <param name="destinationId">The destination id.</param>
    [HttpPost]
    [ContactListLockedExceptionFilter(Texts.UnableToDeleteOrEditFolderOneOrMoreOfTheListsContainedWithinOrTheirSourcesAreInUse)]
    [ValidateHttpAntiForgeryToken]
    public void MoveFolder(string id, string destinationId)
    {
      this.listManager.MoveFolder(id, destinationId);
    }

    /// <summary>
    /// The move list.
    /// </summary>
    /// <param name="id">The list id.</param>
    /// <param name="destinationId">The destination id.</param>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public void MoveList(string id, string destinationId)
    {
      this.listManager.MoveList(id, destinationId);
    }

    /// <summary>
    /// The delete folder.
    /// </summary>
    /// <param name="id">The folder id.</param>
    [HttpPost]
    [ContactListLockedExceptionFilter(Texts.UnableToDeleteOrEditFolderOneOrMoreOfTheListsContainedWithinOrTheirSourcesAreInUse)]
    [ValidateHttpAntiForgeryToken]
    public void DeleteFolder(string id)
    {
      this.listManager.DeleteFolder(id);
    }

    /// <summary>
    /// Removes the duplicates.
    /// </summary>
    /// <param name="id">The list identifier.</param>
    /// <returns>Removed duplicates count.</returns>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public int RemoveDuplicates(string id)
    {
      return this.listManager.RemoveDuplicates(id);
    }

    /// <summary>
    /// The create folder.
    /// </summary>
    /// <param name="folderName">The folder name.</param>
    /// <param name="id">The destination.</param>
    /// <returns>The <see cref="HttpResponseMessage"/>.</returns>
    [HttpPost]
    [ValidateHttpAntiForgeryToken]
    public HttpResponseMessage CreateFolder(string folderName, string id)
    {
      folderName = HttpUtility.UrlDecode(folderName);
      if (!string.IsNullOrEmpty(folderName) && ItemUtil.IsItemNameValid(folderName))
      {
        if (id == "~")
        {
          id = string.Empty;
        }

        this.listManager.CreateFolder(folderName, id);
        return this.Request.CreateResponse(HttpStatusCode.Created);
      }

      return this.Request.CreateErrorResponse(HttpStatusCode.BadRequest, Texts.InvalidFolderNamePleaseUsePermittedCharacters);
    }

    /// <summary>
    /// Export contacts contact to a stream.
    /// </summary>
    /// <param name="id">The list id.</param>
    /// <returns>
    /// Response with exported list stream. <see cref="HttpResponseMessage"/>
    /// </returns>
    [HttpGet]
    public HttpResponseMessage ExportContacts(string id)
    {
      var list = this.listManager.FindById(id);
      var exportResult = this.listManager.ExportContacts(list);

      var responseMessage = new HttpResponseMessage
      {
        StatusCode = HttpStatusCode.OK,
        Content = new StreamContent(exportResult.ContactsStream)
      };

      responseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
      responseMessage.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
      {
        FileName = string.Format("{0} {1:d}.csv", exportResult.ContactListName, DateUtil.ToServerTime(DateTime.UtcNow.Date))
      };

      return responseMessage;
    }

    private class AddNewContactModel
    {
      private const int IdentifierMaxLength = 254;
      private const int PreferredEmailMaxLength = 254;
      private const int FirstNameMaxLength = 50;
      private const int SurnameMaxLength = 50;


      [MaxLength(IdentifierMaxLength)]
      [Required]
      public string Identifier { get; set; }

      [MaxLength(PreferredEmailMaxLength)]
      [Required]
      [EmailAddress()]
      public string PreferredEmail { get; set; }

      [MaxLength(SurnameMaxLength)]
      [Required]
      public string Surname { get; set; }

      [MaxLength(FirstNameMaxLength)]
      [Required]
      public string FirstName { get; set; }
    }
  }

  internal static class RepositoryContainer<TContactList, TSourceContactList, TViewModel> where TContactList : ContactList, new() where TSourceContactList : ContactList, new() where TViewModel : ContactListModel, IEntityIdentity, new()
  {
    private static Sitecore.Abstractions.IFactory factory;

    public static IContactFilter<ContactData> GetContactFilter()
    {
      return new ContactFilter();
    }

    public static ListManager<TContactList, ContactData> GetContactListManager()
    {
      return ((ListManager<TContactList, ContactData>)RepositoryContainer<TContactList, TSourceContactList, TViewModel>.Factory.CreateObject("/sitecore/contactListManager", true));
    }

    public static ContactListRepository<TContactList, TSourceContactList, TViewModel> GetContactListRepository()
    {
      return new ContactListRepository<TContactList, TSourceContactList, TViewModel>(RepositoryContainer<TContactList, TSourceContactList, TViewModel>.GetContactListManager(), RepositoryContainer<TContactList, TSourceContactList, TViewModel>.GetSourceListManager());
    }

    public static ContactRepositoryBase GetContactRepository()
    {
      return ((ContactRepositoryBase)RepositoryContainer<TContactList, TSourceContactList, TViewModel>.Factory.CreateObject("/sitecore/contactRepository", true));
    }

    public static ListManager<TContactList, ContactData> GetSegmentedListManager()
    {
      return ((ListManager<TContactList, ContactData>)RepositoryContainer<TContactList, TSourceContactList, TViewModel>.Factory.CreateObject("/sitecore/segmentedListManager", true));
    }

    public static ContactListRepository<TContactList, TSourceContactList, TViewModel> GetSegmentedListRepository()
    {
      return new ContactListRepository<TContactList, TSourceContactList, TViewModel>(RepositoryContainer<TContactList, TSourceContactList, TViewModel>.GetSegmentedListManager(), RepositoryContainer<TContactList, TSourceContactList, TViewModel>.GetSourceListManager());
    }

    public static ListManager<TSourceContactList, ContactData> GetSourceListManager()
    {
      return ((ListManager<TSourceContactList, ContactData>)RepositoryContainer<TContactList, TSourceContactList, TViewModel>.Factory.CreateObject("/sitecore/contactListManager", true));
    }

    public static Sitecore.Abstractions.IFactory Factory
    {
      get
      {
        return (RepositoryContainer<TContactList, TSourceContactList, TViewModel>.factory ?? (RepositoryContainer<TContactList, TSourceContactList, TViewModel>.factory = new FactoryWrapper()));
      }
      set
      {
        Assert.ArgumentNotNull(value, "value");
        RepositoryContainer<TContactList, TSourceContactList, TViewModel>.factory = value;
      }
    }
  }
}