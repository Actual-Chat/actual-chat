# UI

## Chat list - search in contacts/users/authors

- Special button "Interactive search" (i.e. in search box) which opens a new "Search Chat with search bot"
- Show list of found chats/contacts/places
- Highlight tokens in found items

## Interactive search chat

- User can tweak/focus/make search query more precise by typing/dictating new messages
- Probably by default when query is not recognized as a question (i.e. too short, or just a set of not related keywords)
  we must perform simple/keyword/prefix search
- User can undone/remove last message - hence remove search query to previous state
- Probably, show final/normalized search query in case it was normalized by engine
- Search in contacts
- Search in chat messages
- Scoped search:
    - Place
    - Chat

### Searching for an answer by typing a question. Vector search

- [Show show list of answers in a chat view (maybe a new special view)](#interactive-search-results-view)
- Every answer item
    - short answer
    - links to related entries
- Related entries can be

### Search in places

TBD

## Interactive search results view

- Show list of chats/contacts/places and highlight tokens
    - Items are clickable
    - Clicking on item must lead to
        - contacts, chats
        - [message blocks](#index-for-vss)
- Show a bunch of found messages
    - Date, excerpt
    - Highlight tokens in excerpt
    - Clicking on item must navigate to entry in chat
    - Allow navigating back to search results

## Tag search

TBD

---

# Search Engine

- Identify if a query is a question or just a keyword search query

---

# Indexing

- Rebuild index when [index schema version](#index-verisoning) has changed
- Scoped indexes
    - If content is public or available on a parent level, than put it into a scoped index
        - i.e. index per place
    - TODO: what needs to be in a scoped index?
  - Refresh scoped index when chat is migrated to a place
  - Refresh scoped index when chat became private or public

## History Indexing / Reindexing

- Sort of a job collecting all the chats that are not indexed yet and pushing to index
- Indexing API which sends all the content to indexes, search engines, etc

## Realtime indexing

All changed messages should cause realtime indexing

## Index for VSS

To minimize overhead let's combine multiple message into blocks
Example criteria:

- Bunch of short serial messages
- Maybe try to combine related messages into a block

## Index versioning

- Index schema must contain version
- When there are changes in a schema we need to bump index version
- When deployment happens, new replicas start working with a newer index version
- Older replicas continue working with a previous index version
- Ideally, we shouldn't switch to a newer version until new version index ready to use to avoid search breaking on index rebuild
