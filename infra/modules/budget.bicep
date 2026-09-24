// Maandbudget met e-mailwaarschuwing op 80 % (werkelijk) en 100 % (verwacht).
param environmentName string
param amount int
param startDate string
param contactEmails array

resource budget 'Microsoft.Consumption/budgets@2026-06-01' = {
  name: 'budget-dvd-${environmentName}'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: contactEmails
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: contactEmails
      }
    }
  }
}
