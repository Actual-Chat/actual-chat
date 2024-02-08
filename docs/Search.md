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
# Architecture
### Assumptions:
- Expect to have multiple independent Search Engines.
- Should be able to combine or group results.
- (Optional) Should be able to have feedback on results
  for iterative improvements.
- Most (99%) users would have less than 1k chats to search in.
  Thought there might be outliers.

### Thoughts: 
#### Document access claims
There will be tons of documents that are scoped to private chats 
or in private places that not accessible to all users.
Filtering out result on the client-side would make lots of
returned results wasted.

This leads to a conclusion that it makes sense to have 
access-permissions (claims) for each document.
Strictly speaking each request must be accommodated with 
a set of claims. 

Alternative: A filter must be able to have filters
powerful enough to filter out documents not accessible
by a user (executable lambdas on the servide side). 
This seems to be an option that is way harder to implement
and it still would have little to no benefits
over the first option.

#### Opinionated Search Engine Provider
In the initial implementation there would be a single service
running on the same machine as the requester application.
However with the growth of the number of documents to index
the index size would grow and it would require some kind of sharding
adopted. Most probably it would be time based partitioning first
and documents sharding by chat id's later. However implementation
details are unknown at the moment. Given that it is probably better
to assume each Search Engine to provide its own Adapter for 
the client applications - Search Engine Provider. 

Search Engine Provider is responsible for:
- Convert application filter calls into the corresponding calls
  to their search engine.
- Convert inner application events into corresponding events in 
  the Search Engines.
- Provide document access in case of pull model of the Search Engine.
- Handle Search Engine availability / backpressure.
- Implementation based: track query leader, load balancing if needed, etc.

Search Engine Provider can be implemented as a set of different modules
where each module has it's single responsibility.

#### Minimal result
(Option) Each result must have:
- Items list:
  - URI of the document
  - Search result score of the document
- Cursor (continuation) / or null for the end of results.

#### Cursor (continuation) and re-indexing
Cursors should be immutable to document appends and the resulting
re-indexing. It should also be tolerant to document updates.

The later can be achieved by an assumption that a document delete
is the same thing as a claim changed from read:xyz to read-deleted:xyz.
And an assumption that an update is an equivalent to delete+append.

#### Request result item decorators
It is very beneficial to add document fragments to 
result item. It is possible to have other decorator
types added there (like author, date, etc). 
To achieve this extensibility a search engine can
attach decorators to result items. It is client side
responsibility (Search Engine Provider) to be able
to parse those results. 

Notes: 

(Nice to have) Search Engine Provider 
should be able to send what kind of decorators it 
wants to have. Although it should not be guaranteed
that all or any resulting items have those decorators
in a result.

It is also possible to decorate result itself.
For example a search engine may return an approximate number
of documents remaining matching the filter. It can also
add a decorator indicating that the remaining documents 
are older than certain age.

#### Service Backpressure and Availability
It can be implemented in any form but should be handled
on the ISearchEngineProvider side. It must be able to indicate
in a response that this service is not available at the moment.

It is also required to set corresponding events in to 
a monitoring infrastructure for warn and alerts if needed.

### Proposed solution:
Questions...:
- How to implement a registry for multiple ~~ISearchBackend~~ 
  ISeachEngineProvider instances?
  
  Note: They can be of the same instance type (with different configurations). 
- ~~Is it possible/needed to add another ISearchBackend instance at runtime?~~
  - Possible. Not needed now.

(Opinion) It seems that we can have a single implementation of ISearchBackend
that would have access to different ISearchEngineProviders. It would be responsible
to multiplex search queries and aggregate results returned in the timed manner.
It must set corresponding events into a monitoring infrastructure 
for SearchEngineProviders that missed their time.
It must throttle requests to non-responsive SearchEngineProviders.


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
