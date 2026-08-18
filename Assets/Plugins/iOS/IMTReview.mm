#import <StoreKit/StoreKit.h>
#import <UIKit/UIKit.h>

// Apple'ın kendi değerlendirme sayfası. Sahnesiz requestReview kullanımdan kalktı ve dağıtım hedefi
// 15.0 olduğu için sahneli varyant tek doğru yol. Hiçbir şey döndürmez: sistem kotayı kendi tutar,
// sayfa hiç görünmeyebilir, o yüzden çağıran taraf sonuca bağlı bir UI kurmamalı.
extern "C" void _IMTRequestReview(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
            if ([scene isKindOfClass:[UIWindowScene class]] &&
                scene.activationState == UISceneActivationStateForegroundActive) {
                [SKStoreReviewController requestReviewInScene:(UIWindowScene *)scene];
                return;
            }
        }
    });
}
