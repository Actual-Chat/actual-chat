# Media Processing Improvement Plan

## Цель
Улучшить UX загрузки медиа-файлов: показывать thumbnails сразу, а полные файлы после обработки.

## Текущая проблема
- Файлы проходят 2 этапа: загрузка + постобработка
- Пока постобработка не закончена, другие пользователи не видят файлы
- Особенно критично для видео (FFmpeg конвертация может быть долгой)

## Архитектура решения

### 1. Расширение модели Media
**Файл**: `src/dotnet/Api/Media/Media.cs`

Добавить в `PropertyBag Metadata`:
- `IsProcessing: bool` - флаг процесса обработки
- `ProcessingStatus: string` - статус: "processing" | "completed" | "failed"
- `OriginalMediaId: MediaId?` - ссылка на оригинальный файл (для raw download при ошибке)
- `ProcessingError: string?` - описание ошибки обработки

### 2. Модификация Upload Pipeline
**Файл**: `src/dotnet/Chat.Service/Controllers/ChatMediaController.cs`

#### 2.1. Изменить метод `Upload` (строка 24)
- Генерировать thumbnail быстро (для видео через FFmpeg snapshot)
- Сохранить оригинальный файл с `IsProcessing=true`
- Вернуть `MediaContent` с thumbnail
- Запустить фоновую обработку через `ICommander`

#### 2.2. Изменить метод `UploadChunk` (строка 144)
- Аналогично: быстрый thumbnail → фоновая обработка

### 3. Разделение Upload Processors

#### 3.1. Новый интерфейс `IUploadProcessor`
**Файл**: `src/dotnet/Core.Server/Uploads/IUploadProcessor.cs`

Добавить метод:
```csharp
Task<UploadedTempFile?> GenerateThumbnailOnly(UploadedTempFile upload, CancellationToken ct);
```

#### 3.2. Обновить `ImageUploadProcessor`
**Файл**: `src/dotnet/Core.Server/Uploads/ImageUploadProcessor.cs`

- Реализовать `GenerateThumbnailOnly()` - быстрая генерация preview
- Существующий `Process()` остается для полной обработки

#### 3.3. Обновить `VideoUploadProcessor`
**Файл**: `src/dotnet/Core.Server/Uploads/VideoUploadProcessor.cs`

- Реализовать `GenerateThumbnailOnly()` - использовать существующий `GetThumbnail()`
- Существующий `Process()` остается для конвертации

### 4. Background Processing

#### 4.1. Новая команда `MediaBackend_ProcessUpload`
**Файл**: `src/dotnet/Media.Service/Commands/MediaBackend_ProcessUpload.cs` (создать)

```csharp
public record MediaBackend_ProcessUpload(
    MediaId MediaId,
    UploadedFile OriginalFile
) : ICommand<Media?>;
```

#### 4.2. Command Handler в `MediaBackend`
**Файл**: `src/dotnet/Media.Service/MediaBackend.cs`

Добавить метод:
```csharp
public virtual async Task<Media?> OnProcessUpload(
    MediaBackend_ProcessUpload command,
    CancellationToken cancellationToken)
{
    // 1. Выполнить полную обработку через UploadProcessors
    // 2. Сохранить обработанный файл
    // 3. Обновить Media: IsProcessing=false, ProcessingStatus="completed"
    // 4. При ошибке: ProcessingStatus="failed", сохранить OriginalMediaId
}
```

#### 4.3. Добавить поддержку Update в `MediaBackend.OnChange`
**Файл**: `src/dotnet/Media.Service/MediaBackend.cs` (строка 88)

Заменить:
```csharp
else
    throw new NotSupportedException("Update is not supported.");
```

На:
```csharp
else if (change.IsUpdate(out media)) {
    var dbMedia = await dbContext.Media
        .Get(mediaId.Value, cancellationToken)
        .ConfigureAwait(false);
    if (dbMedia != null) {
        dbMedia.UpdateFrom(media);
    }
}
```

### 5. Модификация MediaStorage
**Файл**: `src/dotnet/Chat.Service/MediaStorage.cs`

#### 5.1. Добавить метод `SaveWithProcessing`
```csharp
public async Task<(Media.Media media, Media.Media? thumbnail)> SaveWithProcessing(
    ChatId chatId,
    UploadedFile originalFile,
    UploadedFile? thumbnailFile,
    Size? size,
    CancellationToken cancellationToken)
{
    // 1. Сохранить оригинал с IsProcessing=true
    // 2. Сохранить thumbnail (если есть)
    // 3. Запустить background обработку через Commander.Run()
    // 4. Вернуть media + thumbnail
}
```

### 6. Client-Side Changes (опционально)

**Что показывать клиентам**:
- `IsProcessing=true, thumbnail != null` → показать thumbnail + spinner
- `IsProcessing=false, ProcessingStatus="completed"` → показать полный файл
- `ProcessingStatus="failed", OriginalMediaId != null` → показать "Download raw file" кнопку

## Порядок реализации

1. ✅ Составить план
2. ⏳ Расширить модель `Media` (добавить поля в metadata)
3. ⏳ Добавить `GenerateThumbnailOnly()` в processors
4. ⏳ Создать команду `MediaBackend_ProcessUpload` + handler
5. ⏳ Добавить поддержку Update в `MediaBackend.OnChange`
6. ⏳ Добавить `SaveWithProcessing()` в `MediaStorage`
7. ⏳ Обновить `ChatMediaController.Upload()`
8. ⏳ Обновить `ChatMediaController.UploadChunk()`
9. ⏳ Тестирование

## Преимущества

- ✅ Thumbnails показываются мгновенно
- ✅ Пользователи видят прогресс обработки
- ✅ Fusion автоматически обновляет UI через computed methods
- ✅ При ошибках можно скачать оригинал
- ✅ Масштабируемость: тяжелая обработка не блокирует API

## Риски и митигация

**Риск**: Background job может упасть
**Митигация**: Сохранять OriginalMediaId, показывать кнопку "Download raw file"

**Риск**: Рост использования диска (оригинал + обработанный)
**Митигация**: Cleanup job для удаления оригиналов после успешной обработки

**Риск**: Конкурентность при chunked upload
**Митигация**: Использовать существующий `ConcurrentDictionary` для координации
