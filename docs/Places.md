# Places

## ChatId
- New ChatKind was introduced: ChatKind.Place
- With its own format: s-{PlaceId}-{ChatIdInsidePlace}
- Chats of the same **Place** has the same id prefix: s-{PlaceId}

## Root chat as a Place
- Place is represented internally as a chat entity. This chat is called place root chat
- Place root chat has special ChatId format: s-{PlaceId}-{PlaceId}
- Root chat contains such information as place title, place icon, flag whether place is public or private
- Root chat authors represent place members
- When a user joins a place, an author belonging to the place root chat is created
- Creating root chat author leads to saving a DbPlaceContact record to contacts db.
- DbPlaceContact records are used to build a list of places an user joined to.

## Contacts
- DbContact record was extended with PlaceId property. PlaceId value is extracted from ChatId property of a contact
- This PlaceId property is used to filter contacts depending on requested place
- Contacts with empty PlaceId property refer to regular chats that existed before places were introduced
- Contact list for a place is built as union of
  - Contacts the user explicitly have for the place
  - Contacts for all public chats of the place
