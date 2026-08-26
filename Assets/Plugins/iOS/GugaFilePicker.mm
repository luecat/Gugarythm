#import <UIKit/UIKit.h>
#import "Unity/UnityInterface.h"

#include <stdlib.h>
#include <string.h>

static NSString *GugaPendingResult = nil;
static BOOL GugaPickerPresented = NO;

static void GugaSetPendingResult(NSString *result)
{
    @synchronized ([NSObject class])
    {
        GugaPendingResult = [result copy];
    }
}

static NSString *GugaErrorResult(NSString *message)
{
    NSString *safeMessage = message.length > 0 ? message : @"未知錯誤";
    safeMessage = [safeMessage stringByReplacingOccurrencesOfString:@"\n" withString:@" "];
    return [@"ERROR:" stringByAppendingString:safeMessage];
}

static UIViewController *GugaTopViewController(void)
{
    UIViewController *controller = UnityGetGLViewController();
    while (controller.presentedViewController != nil)
        controller = controller.presentedViewController;
    return controller;
}

@interface GugaFilePickerDelegate : NSObject <UIDocumentPickerDelegate>

+ (instancetype)sharedDelegate;
- (void)openFilePicker;

@end

@implementation GugaFilePickerDelegate

+ (instancetype)sharedDelegate
{
    static GugaFilePickerDelegate *delegate;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        delegate = [[GugaFilePickerDelegate alloc] init];
    });
    return delegate;
}

- (void)openFilePicker
{
    if (GugaPickerPresented) return;

    UIViewController *controller = GugaTopViewController();
    if (controller == nil)
    {
        GugaSetPendingResult(GugaErrorResult(@"無法開啟 iOS 文件選擇器。"));
        return;
    }

    UIDocumentPickerViewController *picker =
        [[UIDocumentPickerViewController alloc] initWithDocumentTypes:@[@"public.data"]
                                                              inMode:UIDocumentPickerModeImport];
    picker.delegate = self;
    picker.allowsMultipleSelection = YES;
    picker.modalPresentationStyle = UIModalPresentationFormSheet;
    GugaPickerPresented = YES;
    [controller presentViewController:picker animated:YES completion:nil];
}

- (void)documentPicker:(UIDocumentPickerViewController *)controller
didPickDocumentsAtURLs:(NSArray<NSURL *> *)urls
{
    GugaPickerPresented = NO;

    NSMutableArray<NSString *> *results = [NSMutableArray arrayWithCapacity:urls.count];
    NSFileManager *fileManager = [NSFileManager defaultManager];
    NSURL *temporaryRoot = [NSURL fileURLWithPath:NSTemporaryDirectory() isDirectory:YES];
    temporaryRoot = [temporaryRoot URLByAppendingPathComponent:@"GugarhythmImports" isDirectory:YES];

    for (NSURL *sourceURL in urls)
    {
        if ([sourceURL.pathExtension caseInsensitiveCompare:@"ggr"] != NSOrderedSame)
        {
            [results addObject:GugaErrorResult(@"請選擇 GGR 封包。")];
            continue;
        }

        BOOL accessing = [sourceURL startAccessingSecurityScopedResource];
        NSString *folderName = [[NSUUID UUID] UUIDString];
        NSURL *destinationFolder = [temporaryRoot URLByAppendingPathComponent:folderName isDirectory:YES];
        NSURL *destinationURL = [destinationFolder URLByAppendingPathComponent:sourceURL.lastPathComponent];
        NSError *error = nil;

        if (![fileManager createDirectoryAtURL:destinationFolder
                   withIntermediateDirectories:YES
                                    attributes:nil
                                         error:&error] ||
            ![fileManager copyItemAtURL:sourceURL toURL:destinationURL error:&error])
        {
            [results addObject:GugaErrorResult(
                [NSString stringWithFormat:@"iOS 無法讀取 GGR：%@", error.localizedDescription])];
        }
        else
        {
            [results addObject:destinationURL.path];
        }

        if (accessing) [sourceURL stopAccessingSecurityScopedResource];
    }

    if (results.count > 0)
        GugaSetPendingResult([results componentsJoinedByString:@"\n"]);
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController *)controller
{
    GugaPickerPresented = NO;
}

@end

extern "C"
{
    void GugaOpenFile(void)
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            [[GugaFilePickerDelegate sharedDelegate] openFilePicker];
        });
    }

    const char *GugaConsumeResult(void)
    {
        @synchronized ([NSObject class])
        {
            if (GugaPendingResult.length == 0) return NULL;
            char *result = strdup(GugaPendingResult.UTF8String);
            GugaPendingResult = nil;
            return result;
        }
    }

    void GugaFreeString(const char *value)
    {
        free((void *)value);
    }
}
