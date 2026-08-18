#import <AppTrackingTransparency/AppTrackingTransparency.h>
#import <Foundation/Foundation.h>

typedef void (*IMTTrackingCallback)(int status);

// 0 NotDetermined, 1 Restricted, 2 Denied, 3 Authorized — ATTrackingManagerAuthorizationStatus ile aynı.
extern "C" int _IMTTrackingStatus(void)
{
    if (@available(iOS 14.0, *)) return (int)ATTrackingManager.trackingAuthorizationStatus;
    return 3;   // iOS 14 öncesinde sorulacak bir şey yok, IDFA zaten açık
}

extern "C" void _IMTRequestTracking(IMTTrackingCallback callback)
{
    if (@available(iOS 14.0, *)) {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:
            ^(ATTrackingManagerAuthorizationStatus status) {
                dispatch_async(dispatch_get_main_queue(), ^{ if (callback) callback((int)status); });
            }];
    } else if (callback) {
        callback(3);
    }
}

// UMP, TCF dizelerini NSUserDefaults'a yazar. Unity'nin PlayerPrefs'i de oraya bakar ama anahtar
// biçimi garanti değil; SDK'nın yazdığı yerden okumak tek kesin yol. Dönen kopyayı Mono serbest
// bırakır, o yüzden malloc ile ayrılmalı.
extern "C" const char* _IMTUserDefaultsString(const char* key)
{
    if (key == NULL) return NULL;
    NSString *value = [[NSUserDefaults standardUserDefaults]
                       stringForKey:[NSString stringWithUTF8String:key]];
    if (value == nil) return NULL;

    const char *utf8 = [value UTF8String];
    if (utf8 == NULL) return NULL;

    char *copy = (char *)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}
